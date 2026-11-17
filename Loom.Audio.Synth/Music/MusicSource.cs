using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// The sequencer: plays a compiled song on a chip core, on the audio clock. Each render it splits the block at every
/// event, bar line and section end, handing the core its notes between slices, so timing is exact to the frame.
/// Everything it needs comes made (<see cref="SongState"/>); it allocates nothing.
/// </summary>
internal sealed class MusicSource : AudioSource
{
	private const int PendingCapacity = 256;
	private const double LevelSeconds = 0.15;

	// A millionth of a tick: the clock adds a tick count that isn't exact in binary each frame, and would land a hair
	// short of a beat it should be on
	private const double Epsilon = 1e-6;

	// The song's own chip (SongState.Core), replaced at a swap; the game thread reads its stats
	private volatile ChipCore _core;
	// The chip a swap replaced, rendered on for a while so its notes and effects' tails ring out rather than stop: the
	// frames left, and where in the block the swap fell
	private const double OutgoingSeconds = 2.0;
	private const double OutgoingFadeSeconds = 0.25;
	private ChipCore? _outgoing;
	private int _outgoingLeft;
	private int _outgoingFrom;
	private readonly float[] _outgoingL = new float[AudioEffect.MaxBlockFrames];
	private readonly float[] _outgoingR = new float[AudioEffect.MaxBlockFrames];
	private readonly AudioEventQueue<MusicCommand> _commands = new(256);
	private SongState _state;
	private int _rate;

	// The clock: the tick within the section at the block's first frame, and ticks a frame
	private double _tick;
	private double _ticksPerFrame;
	private long _frame;
	private double _nextBar;
	private double _nextEvent = double.MaxValue;

	private int _section;
	private int _orderPosition;
	private int _pass = 1;
	private long _bar = -1;
	private int _next = -1;
	private bool _loop;
	private int _jump = -1;
	private SongState? _swap;
	private int _noteId;

	// Notes a humanised track holds back, due at a frame
	private readonly long[] _pendingDue = new long[PendingCapacity];
	private readonly ChipEvent[] _pendingEvent = new ChipEvent[PendingCapacity];
	private int _pendingCount;
	private long _nextPending = long.MaxValue;

	private readonly bool[] _stack = new bool[CompiledSong.ConditionDepth];

	// Published for the game thread
	private volatile int _publishedSection;
	private volatile int _publishedBar = 1;
	private volatile int _publishedBeat = 1;
	private long _publishedAbsoluteBar;
	private volatile int _publishedTicksLeft = int.MaxValue;
	private volatile int _publishedPass = 1;
	private volatile int _sectionStarts;
	private volatile SongState _publishedState;

	public MusicSource(SongState state)
	{
		_state = state;
		_publishedState = state;
		_core = state.Core;
		_section = state.Song.Order.Length > 0 ? state.Song.Order[0] : 0;
	}

	public ChipCore Core => _core;
	public ChipStats Stats => _core.Stats;

	// What the game reads
	public SongState State => _publishedState;
	public int SectionIndex => _publishedSection;
	public int Bar => _publishedBar;
	public int Beat => _publishedBeat;
	public long AbsoluteBar => Volatile.Read(ref _publishedAbsoluteBar);
	public int TicksLeft => _publishedTicksLeft;
	public int Pass => _publishedPass;
	public int SectionStarts => _sectionStarts;

	// Game thread: a command for the next block
	public void Post(in MusicCommand command)
	{
		if (!_commands.TryPost(command))
			throw new InvalidOperationException("The music's command queue is full: is it playing?");
	}

	// Game thread: a cue's value, by its slot in a state's song
	public static void SetCue(SongState state, int slot, float value) => Volatile.Write(ref state.Cues[slot], value);

	protected override void OnStart(int sampleRate)
	{
		if (_rate != 0)
			throw new InvalidOperationException("A song's player plays its source once: play the song again for another.");
		_rate = sampleRate;
		_state.PrepareCore(sampleRate);
		_ticksPerFrame = TicksPerFrame(_state.Song);
		for (var slot = 0; slot < _state.Levels.Length; slot++)
			_state.Levels[slot] = -1f;
	}

	private double TicksPerFrame(CompiledSong song) => song.Tempo * Ticks.PerBeat / (60.0 * _rate);

	protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
	{
		TakeCommands();
		_core.ClearPeaks();
		var n = info.Frames;
		Steer(n);

		var start = 0;
		for (var f = 0; f < n; f++)
		{
			var frame = _frame + f;
			while (true)
			{
				var t = _tick + f * _ticksPerFrame + Epsilon;
				var song = _state.Song;
				var length = song.Sections[_section].LengthTicks;
				var ended = t >= length;
				var bar = t >= _nextBar;
				var due = t >= _nextEvent;
				var pending = frame >= _nextPending;
				if (!ended && !bar && !due && !pending)
					break;
				if (f > start)
				{
					_core.Render(left[start..f], right[start..f]);
					start = f;
				}
				if (ended)
					EndSection(length);
				else if (bar)
					BarLine(f, frame);
				else if (due)
					Fire(t, frame);
				else
					FirePending(frame);
			}
		}
		_core.Render(left[start..n], right[start..n]);
		RenderOutgoing(left, right, n);
		_tick += n * _ticksPerFrame;
		_frame += n;
		Publish();
		return true;
	}

	// The chip a swap replaced, from where in the block the swap fell, added in under a fade over its last moments
	private void RenderOutgoing(Span<float> left, Span<float> right, int n)
	{
		if (_outgoing is not { } outgoing)
			return;
		var from = _outgoingFrom;
		_outgoingFrom = 0;
		var frames = n - from;
		if (frames <= 0)
			return;
		var l = _outgoingL.AsSpan(0, frames);
		var r = _outgoingR.AsSpan(0, frames);
		l.Clear();
		r.Clear();
		outgoing.Render(l, r);
		var fadeFrames = (int)(OutgoingFadeSeconds * _rate);
		for (var s = 0; s < frames; s++)
		{
			var gain = Math.Min(1f, (float)(_outgoingLeft - s) / fadeFrames);
			if (gain <= 0f)
				break;
			left[from + s] += l[s] * gain;
			right[from + s] += r[s] * gain;
		}
		_outgoingLeft -= frames;
		if (_outgoingLeft <= 0)
			_outgoing = null;
	}

	private void TakeCommands()
	{
		while (_commands.TryTake(out var command))
		{
			var state = _state;
			switch (command.Kind)
			{
				case MusicCommandKind.Swap:
					_swap = (SongState)command.Payload!;
					break;
				case MusicCommandKind.Next:
					_next = command.A;
					break;
				case MusicCommandKind.Jump:
					_jump = command.A;
					break;
				case MusicCommandKind.Loop:
					_loop = command.Value != 0f;
					break;
				case MusicCommandKind.Mute:
					state.Muted[command.A] = command.Value != 0f;
					ApplyMutes(state);
					break;
				case MusicCommandKind.Solo:
					state.Soloed[command.A] = command.Value != 0f;
					ApplyMutes(state);
					break;
				case MusicCommandKind.TrackGain:
					state.TrackGain[command.A] = command.Value;
					state.Instruments[command.A].Gain = state.Song.Tracks[command.A].Gain * command.Value;
					break;
				case MusicCommandKind.Force:
					state.Forced[command.A][command.B] = (PlacementState)(int)command.Value;
					break;
			}
		}
	}

	// A track is silent while muted, or while another is soloed and it isn't
	private static void ApplyMutes(SongState state)
	{
		var anySolo = false;
		foreach (var solo in state.Soloed)
			anySolo |= solo;
		for (var t = 0; t < state.Instruments.Length; t++)
			state.Instruments[t].Muted = state.Muted[t] || (anySolo && !state.Soloed[t]);
	}

	// Once a block: each part's level eased toward its cue into its gain slot, and each macro a track's song steers
	private void Steer(int n)
	{
		var state = _state;
		var song = state.Song;
		var keep = (float)Math.Exp(-n / (LevelSeconds * _rate));
		foreach (var placement in song.Sections[_section].Placements)
		{
			if (placement.GainSlot < 0)
				continue;
			var target = Math.Clamp(Volatile.Read(ref state.Cues[placement.LevelSlot]), 0f, 1f);
			ref var level = ref state.Levels[placement.GainSlot];
			level = level < 0f ? target : target + (level - target) * keep;
			_core.GainSlots[placement.GainSlot] = level;
		}
		for (var t = 0; t < song.Tracks.Length; t++)
			foreach (var macro in song.Tracks[t].Macros)
			{
				var value = macro.Slot < 0 ? macro.From : macro.From + (macro.To - macro.From) * Volatile.Read(ref state.Cues[macro.Slot]);
				state.Instruments[t].Macros[macro.Macro] = Math.Clamp(value, 0f, 1f);
			}
		// A macro the section sweeps is where the section has got to, over whatever its track said
		var section = song.Sections[_section];
		if (section.Sweeps.Length > 0)
		{
			var through = (float)Math.Clamp(_tick / section.LengthTicks, 0.0, 1.0);
			foreach (var sweep in section.Sweeps)
				state.Instruments[sweep.Track].Macros[sweep.Macro] = Math.Clamp(sweep.From + (sweep.To - sweep.From) * through, 0f, 1f);
		}
	}

	// The section's end: the next one decided, else the next in the song's order, else the same again; Loop holds it
	private void EndSection(int length)
	{
		var song = _state.Song;
		var next = _section;
		if (_loop)
			next = _section;
		else if (_next >= 0 && _next < song.Sections.Length)
		{
			next = _next;
			var at = Array.IndexOf(song.Order, next);
			if (at >= 0)
				_orderPosition = at;
		}
		else if (song.Order.Length > 0)
		{
			_orderPosition = (_orderPosition + 1) % song.Order.Length;
			next = song.Order[_orderPosition];
		}
		_next = -1;
		_tick -= length;
		Begin(next, sameCounts: next == _section);
	}

	// A section starting at tick 0, which is then a bar line and a phrase start
	private void Begin(int section, bool sameCounts)
	{
		_pass = sameCounts ? _pass + 1 : 1;
		_section = section;
		_nextBar = 0;
		_nextEvent = double.MaxValue;
		Array.Clear(_state.Active);
		Array.Clear(_state.NoteCount);
		_sectionStarts++;
	}

	private void BarLine(int f, long frame)
	{
		var barTick = _nextBar;
		if (_swap is { } swap)
		{
			TakeSwap(swap, barTick, f);
			return;
		}
		if (_jump >= 0)
		{
			var target = Math.Min(_jump, _state.Song.Sections.Length - 1);
			_jump = -1;
			// The jump's section starts at this frame
			_tick = -f * _ticksPerFrame;
			Begin(target, sameCounts: false);
			var at = Array.IndexOf(_state.Song.Order, target);
			if (at >= 0)
				_orderPosition = at;
			return;
		}

		_bar++;
		var song = _state.Song;
		_nextBar = barTick + song.TicksPerBar;
		var placements = song.Sections[_section].Placements;
		var forced = _state.Forced[_section];
		var bar = (int)Math.Round(barTick);
		for (var p = 0; p < placements.Length; p++)
		{
			var placement = placements[p];
			// An on-phrase part is asked only where it starts over; an on-bar one at every bar line
			if (placement.OnPhrase && bar % placement.LoopTicks != 0)
				continue;
			var wanted = forced[p] switch
			{
				PlacementState.On => true,
				PlacementState.Off => false,
				_ => Holds(placement.Condition),
			};
			if (wanted && !_state.Active[p])
			{
				_state.Active[p] = true;
				var events = placement.Events;
				var cursor = 0;
				while (cursor < events.Length && events[cursor].Tick < bar)
					cursor++;
				_state.Cursor[p] = cursor;
			}
			else if (!wanted && _state.Active[p])
			{
				_state.Active[p] = false;
				LetGo(p, frame);
			}
		}
		NextEvent();
	}

	// The song rebuilt (or another), taken at a bar line: the section of the same name, at the same bar, the new tempo, on
	// the new song's own chip. The old chip lets go of its notes and rings out under the new one for a while
	private void TakeSwap(SongState swap, double barTick, int f)
	{
		_swap = null;
		_core.Handle(new ChipEvent { Kind = ChipEventKind.AllOff });
		_outgoing = _core;
		_outgoingLeft = (int)(OutgoingSeconds * _rate);
		_outgoingFrom = f;
		_core = swap.Core;
		// What a humanised track held back was meant for the old song's instruments
		_pendingCount = 0;
		_nextPending = long.MaxValue;

		var section = _section < swap.SectionMap.Length ? swap.SectionMap[_section] : 0;
		var song = swap.Song;
		var tick = barTick < song.Sections[section].LengthTicks ? barTick : 0;
		for (var slot = 0; slot < swap.Levels.Length; slot++)
			swap.Levels[slot] = -1f;
		_state = swap;
		_publishedState = swap;
		_section = section;
		_ticksPerFrame = TicksPerFrame(song);
		_tick = tick - f * _ticksPerFrame;
		_nextBar = tick;
		_nextEvent = double.MaxValue;
		_next = -1;
		if (song.Order.Length > 0)
			_orderPosition = Math.Max(0, Array.IndexOf(song.Order, section));
		ApplyMutes(swap);
	}

	// Whether a placement's condition holds now, run over its postfix steps
	private bool Holds(ConditionOp[] program)
	{
		if (program.Length == 0)
			return true;
		var cues = _state.Cues;
		var latches = _state.Latches;
		var top = 0;
		foreach (var op in program)
		{
			switch (op.Kind)
			{
				case ConditionOpKind.Above:
					{
						var value = Volatile.Read(ref cues[op.Slot]);
						var on = latches[op.Latch] ? value >= op.Value - 0.05f : value >= op.Value;
						latches[op.Latch] = on;
						_stack[top++] = on;
						break;
					}
				case ConditionOpKind.Below:
					{
						var value = Volatile.Read(ref cues[op.Slot]);
						var on = latches[op.Latch] ? value <= op.Value + 0.05f : value <= op.Value;
						latches[op.Latch] = on;
						_stack[top++] = on;
						break;
					}
				case ConditionOpKind.Is:
					_stack[top++] = MathF.Abs(Volatile.Read(ref cues[op.Slot]) - op.Value) < 0.5f;
					break;
				case ConditionOpKind.And:
					top--;
					_stack[top - 1] = _stack[top - 1] && _stack[top];
					break;
				case ConditionOpKind.Or:
					top--;
					_stack[top - 1] = _stack[top - 1] || _stack[top];
					break;
				case ConditionOpKind.Not:
					_stack[top - 1] = !_stack[top - 1];
					break;
			}
		}
		return _stack[0];
	}

	// Every active part's events due by tick t
	private void Fire(double t, long frame)
	{
		var song = _state.Song;
		var placements = song.Sections[_section].Placements;
		for (var p = 0; p < placements.Length; p++)
		{
			if (!_state.Active[p])
				continue;
			var placement = placements[p];
			var events = placement.Events;
			ref var cursor = ref _state.Cursor[p];
			while (cursor < events.Length && events[cursor].Tick <= t)
			{
				Dispatch(placement, p, cursor, in events[cursor], frame);
				cursor++;
			}
		}
		NextEvent();
	}

	private void Dispatch(CompiledPlacement placement, int p, int index, in SongEvent e, long frame)
	{
		var state = _state;
		var track = state.Song.Tracks[placement.Track];
		if (state.Instruments[placement.Track].Muted)
			return;
		var velocity = e.Velocity;
		long delay = 0;
		if (track.HumaniseMs > 0f || track.HumaniseVelocity > 0f)
		{
			// Two numbers from the track, the bar and the event, so the song plays the same every time
			var h = track.Seed;
			h = (h ^ (uint)_bar) * 0x9E3779B1u;
			h = (h ^ (uint)index) * 0x85EBCA6Bu;
			h ^= h >> 15;
			var u1 = (h & 0xFFFF) / 65536f;
			var u2 = (h >> 16) / 65536f;
			velocity = Math.Clamp(velocity * (1f + (u2 - 0.5f) * 2f * track.HumaniseVelocity), 0f, 1f);
			delay = (long)(u1 * 2f * track.HumaniseMs * _rate / 1000f);
		}
		ChipEvent chip;
		var frames = 0;
		if (e.Piece >= 0)
			chip = new ChipEvent { Kind = ChipEventKind.Hit, Instrument = placement.Track, PieceIndex = e.Piece, Velocity = velocity, Note = 60f };
		else
		{
			frames = Math.Max(1, (int)(e.Length / _ticksPerFrame));
			chip = new ChipEvent
			{
				Kind = ChipEventKind.NoteOn,
				Instrument = placement.Track,
				NoteId = ++_noteId,
				Note = e.Note,
				Velocity = velocity,
				Pan = track.Pan,
				Frames = frames,
			};
			Remember(p, chip.NoteId, frame + delay + frames);
		}
		chip.GainSlot = placement.GainSlot + 1;
		if (delay > 0 && _pendingCount < PendingCapacity)
		{
			_pendingDue[_pendingCount] = frame + delay;
			_pendingEvent[_pendingCount] = chip;
			_pendingCount++;
			if (frame + delay < _nextPending)
				_nextPending = frame + delay;
		}
		else
			_core.Handle(in chip);
	}

	// A note sounding for a part, to let go if the part stops
	private void Remember(int p, int id, long end)
	{
		var state = _state;
		var count = state.NoteCount[p];
		var at = p * SongState.NoteMemory;
		if (count == SongState.NoteMemory)
		{
			// Full: the oldest makes room
			Array.Copy(state.NoteIds, at + 1, state.NoteIds, at, SongState.NoteMemory - 1);
			Array.Copy(state.NoteEnds, at + 1, state.NoteEnds, at, SongState.NoteMemory - 1);
			count--;
		}
		state.NoteIds[at + count] = id;
		state.NoteEnds[at + count] = end;
		state.NoteCount[p] = count + 1;
	}

	// A part stopping: each of its notes still sounding let go
	private void LetGo(int p, long frame)
	{
		var state = _state;
		var at = p * SongState.NoteMemory;
		for (var i = 0; i < state.NoteCount[p]; i++)
			if (state.NoteEnds[at + i] > frame)
				_core.Handle(new ChipEvent { Kind = ChipEventKind.NoteOff, NoteId = state.NoteIds[at + i] });
		state.NoteCount[p] = 0;
	}

	private void FirePending(long frame)
	{
		var next = long.MaxValue;
		for (var i = 0; i < _pendingCount;)
		{
			if (_pendingDue[i] <= frame)
			{
				_core.Handle(in _pendingEvent[i]);
				_pendingCount--;
				_pendingDue[i] = _pendingDue[_pendingCount];
				_pendingEvent[i] = _pendingEvent[_pendingCount];
				continue;
			}
			next = Math.Min(next, _pendingDue[i]);
			i++;
		}
		_nextPending = next;
	}

	// The earliest event of any active part
	private void NextEvent()
	{
		var next = double.MaxValue;
		var placements = _state.Song.Sections[_section].Placements;
		for (var p = 0; p < placements.Length; p++)
		{
			if (!_state.Active[p])
				continue;
			var cursor = _state.Cursor[p];
			var events = placements[p].Events;
			if (cursor < events.Length && events[cursor].Tick < next)
				next = events[cursor].Tick;
		}
		_nextEvent = next;
	}

	private void Publish()
	{
		var state = _state;
		var song = state.Song;
		var t = Math.Max(0, _tick + Epsilon);
		_publishedSection = _section;
		_publishedBar = (int)(t / song.TicksPerBar) + 1;
		_publishedBeat = (int)(t % song.TicksPerBar / song.TicksPerBeat) + 1;
		Volatile.Write(ref _publishedAbsoluteBar, Math.Max(0, _bar));
		_publishedTicksLeft = (int)Math.Max(0, song.Sections[_section].LengthTicks - t);
		_publishedPass = _pass;
		Array.Copy(state.Active, state.Published, state.Active.Length);
		var peaks = _core.InstrumentPeak;
		for (var i = 0; i < state.Peaks.Length && i < peaks.Length; i++)
			Volatile.Write(ref state.Peaks[i], peaks[i]);
	}
}
