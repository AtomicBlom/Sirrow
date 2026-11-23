using BinaryVibrance.Sirrow.Synth.Effects;

namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>
/// The rendering half of a <see cref="SynthChip"/>: voices, groups and their effects, the two sends, the master. Not an
/// <see cref="AudioSource"/>, so a sequencer can drive one directly, sample-accurately, by handing it events between
/// renders. Everything is allocated in <see cref="Prepare"/>; handling events and rendering allocate nothing.
/// </summary>
internal sealed class ChipCore
{
	private readonly ChipOptions _options;
	private readonly ChipInstrumentState?[] _instruments;
	private ChipVoice[] _voices = [];
	private float[][] _scratch = [];
	private float[][] _groupLeft = [], _groupRight = [];
	private AudioEffect[][] _groupEffects = [];
	private AudioEffect[] _spaceEffects = [], _echoEffects = [], _masterEffects = [];
	private readonly float[] _spaceLeft = new float[AudioEffect.MaxBlockFrames], _spaceRight = new float[AudioEffect.MaxBlockFrames];
	private readonly float[] _echoLeft = new float[AudioEffect.MaxBlockFrames], _echoRight = new float[AudioEffect.MaxBlockFrames];
	// The key: what voices send to ChipSend.Key, handed to keyed effects and never heard
	private readonly float[] _keyLeft = new float[AudioEffect.MaxBlockFrames];
	private readonly float[] _keyRight = new float[AudioEffect.MaxBlockFrames];
	private readonly float[] _masterLeft = new float[AudioEffect.MaxBlockFrames], _masterRight = new float[AudioEffect.MaxBlockFrames];
	private int _rate;
	private long _started;
	private long _released;
	private int _stolen;
	private volatile int _activeVoices, _activeOperators, _stolenVoices;
	// Counted on the audio thread, read by the game's: notes that found no voice they could take
	private volatile int _droppedNotes;

	public ChipCore(ChipOptions options)
	{
		_options = options;
		_instruments = new ChipInstrumentState?[options.MaxInstruments];

		InstrumentPeak = new float[options.MaxInstruments];
	}

	public bool IsPrepared => _rate > 0;
	public int SampleRate => _rate;
	public ChipStats Stats => new(_activeVoices, _activeOperators, _stolenVoices, _droppedNotes);
	public IReadOnlyList<ChipVoice> Voices => _voices;

	// How many gain slots a core has
	public const int GainSlotCount = 64;

	// Gains a voice follows while it sounds (a song part's level), each 1 until set: a note on or hit names one by
	// ChipEvent.GainSlot. Written by whoever drives the core, on the audio thread
	public readonly float[] GainSlots = Filled(GainSlotCount, 1f);

	// Per instrument, the loudest of its voices since ClearPeaks, after their gain, before the group's effects
	public float[] InstrumentPeak { get; private set; } = [];

	// The instruments' peaks back to 0, to measure from here: once an audio block
	public void ClearPeaks() => Array.Clear(InstrumentPeak);

	// An instrument as the core holds it, for a sequencer that sets its gain, mute and macros directly
	public ChipInstrumentState? Instrument(int index) => _instruments[index];

	private static float[] Filled(int count, float value)
	{
		var array = new float[count];
		Array.Fill(array, value);
		return array;
	}

	/// <summary>Allocates the voices, buffers and effects for <paramref name="rate"/>. Once.</summary>
	public void Prepare(int rate)
	{
		if (_rate == rate)
			return;
		if (_rate != 0)
			throw new InvalidOperationException($"The chip was prepared at {_rate} Hz and can't be played at {rate} Hz: make another chip.");
		_rate = rate;
		_voices = new ChipVoice[_options.Voices];
		for (var i = 0; i < _voices.Length; i++)
			_voices[i] = new ChipVoice(i, rate);
		_scratch = new float[Patch.MaxOperators][];
		for (var i = 0; i < _scratch.Length; i++)
			_scratch[i] = new float[AudioEffect.MaxBlockFrames];
		var groups = _options.Groups;
		_groupLeft = new float[groups.Length][];
		_groupRight = new float[groups.Length][];
		_groupEffects = new AudioEffect[groups.Length][];
		for (var g = 0; g < groups.Length; g++)
		{
			_groupLeft[g] = new float[AudioEffect.MaxBlockFrames];
			_groupRight[g] = new float[AudioEffect.MaxBlockFrames];
			_groupEffects[g] = Make(groups[g].Effects, rate);
		}
		_spaceEffects = Make(_options.SpaceEffects, rate);
		_echoEffects = Make(_options.EchoEffects, rate);
		_masterEffects = Make(_options.MasterEffects, rate);
	}

	private static AudioEffect[] Make(Func<AudioEffect>[] factories, int rate)
	{
		var effects = new AudioEffect[factories.Length];
		for (var i = 0; i < effects.Length; i++)
		{
			effects[i] = factories[i]() ?? throw new InvalidOperationException("An effect factory gave null.");
			effects[i].Prepare(rate);
		}
		return effects;
	}

	/// <summary>Does what <paramref name="e"/> asks, at once.</summary>
	public void Handle(in ChipEvent e)
	{
		switch (e.Kind)
		{
			case ChipEventKind.AddInstrument:
				_instruments[e.Instrument] = (ChipInstrumentState)e.Payload!;
				break;
			case ChipEventKind.ReplaceInstrument:
				var replacing = (ChipInstrumentState)e.Payload!;
				if (_instruments[e.Instrument] is { } was)
				{
					was.Macros.CopyTo(replacing.Macros, 0);
					replacing.Gain = was.Gain;
					replacing.Muted = was.Muted;
					replacing.NoteTurn = was.NoteTurn;
				}
				_instruments[e.Instrument] = replacing;
				break;
			case ChipEventKind.NoteOn:
				NoteOn(in e);
				break;
			case ChipEventKind.NoteOff:
				foreach (var voice in _voices)
				{
					if (voice.Active && voice.NoteId == e.NoteId && !voice.Released)
						voice.Release(++_released);
					// A note still waiting for a stolen voice to fade never starts: nothing would let it go
					if (voice.HasPending && voice.Pending.NoteId == e.NoteId && e.NoteId != 0)
						CancelPending(voice);
				}
				break;
			case ChipEventKind.Hit:
				Hit(in e);
				break;
			case ChipEventKind.Macro:
				if (_instruments[e.Instrument] is { } instrument)
					instrument.Macros[e.Macro] = Math.Clamp(e.Value, 0f, 1f);
				break;
			case ChipEventKind.AllOff:
				foreach (var voice in _voices)
				{
					if (voice.Active)
						voice.Release(++_released);
					if (voice.HasPending)
						CancelPending(voice);
				}
				break;
		}
	}

	// A note waiting for a voice that is fading out for it, let go before it started: the voice fades out and frees
	private static void CancelPending(ChipVoice voice)
	{
		voice.HasPending = false;
		voice.PendingTake = null;
	}

	private int GateFrames(in ChipEvent e) =>
		e.Frames > 0 ? e.Frames : e.Seconds > 0f ? Math.Max(1, (int)(e.Seconds * _rate)) : -1;

	private void NoteOn(in ChipEvent e)
	{
		if (_instruments[e.Instrument] is not { Patch: { } patch } instrument || instrument.Muted)
			return;
		if (patch.Mono)
			foreach (var voice in _voices)
				if (voice.Active && voice.Instrument == instrument && voice.FadeStep == 0f)
				{
					voice.Move(in e, GateFrames(in e));
					return;
				}
		Begin(instrument, patch, in e, null);
	}

	private void Hit(in ChipEvent e)
	{
		if (_instruments[e.Instrument] is not { Kit: { } kit } instrument || instrument.Muted || (uint)e.PieceIndex >= (uint)kit.Pieces.Count)
			return;
		var piece = kit.Pieces[e.PieceIndex];
		var take = piece.Patch is null ? instrument.PickTake(e.PieceIndex, e.Velocity) : null;
		Begin(instrument, instrument.PiecePatches[e.PieceIndex], in e, take);
		if (piece.ChokeGroup <= 0)
			return;
		var chokeStep = 1f / (0.03f * _rate);
		foreach (var voice in _voices)
			if (voice.Active && voice.Instrument == instrument && voice.ChokeGroup == piece.ChokeGroup && voice.PieceIndex != e.PieceIndex)
			{
				voice.Release(++_released);
				if (!voice.HasPending)
					voice.FadeStep = MathF.Max(voice.FadeStep, chokeStep);
			}
	}

	// A note started on a free voice, or on one stolen once it has faded
	private void Begin(ChipInstrumentState instrument, Patch patch, in ChipEvent e, SampleData? take)
	{
		ChipVoice? free = null;
		foreach (var voice in _voices)
			if (!voice.Active)
			{
				free = voice;
				break;
			}
		if (free is not null)
		{
			Start(free, instrument, patch, in e, take);
			return;
		}

		// None free: the one released longest, else the oldest, among those not already fading out for another note
		ChipVoice? victim = null;
		foreach (var voice in _voices)
		{
			if (voice.HasPending)
				continue;
			if (victim is null
				|| (voice.Released && (!victim.Released || voice.ReleasedOrder < victim.ReleasedOrder))
				|| (!voice.Released && !victim.Released && voice.Order < victim.Order))
				victim = voice;
		}
		if (victim is null)
		{
			// Every voice already fading out for another note: this one is lost, and counted
			_droppedNotes++;
			return;
		}
		victim.HasPending = true;
		victim.Pending = e;
		victim.PendingTake = take;
		victim.FadeStep = 1f / 128f;
		_stolen++;
	}

	private void Start(ChipVoice voice, ChipInstrumentState instrument, Patch patch, in ChipEvent e, SampleData? take)
	{
		// A hit gates off after two seconds, by when a struck envelope has died away, so a held one can't sound for ever
		var gate = e.Kind == ChipEventKind.Hit ? 2 * _rate : GateFrames(in e);
		voice.Start(instrument, patch, in e, gate, take, ++_started);
	}

	/// <summary>Renders the next frames, at most <see cref="AudioEffect.MaxBlockFrames"/>, into <paramref name="left"/> and <paramref name="right"/>.</summary>
	public void Render(Span<float> left, Span<float> right)
	{
		var n = left.Length;
		for (var g = 0; g < _groupLeft.Length; g++)
		{
			Array.Clear(_groupLeft[g], 0, n);
			Array.Clear(_groupRight[g], 0, n);
		}
		Array.Clear(_spaceLeft, 0, n);
		Array.Clear(_spaceRight, 0, n);
		Array.Clear(_echoLeft, 0, n);
		Array.Clear(_echoRight, 0, n);
		Array.Clear(_keyLeft, 0, n);
		Array.Clear(_keyRight, 0, n);
		Array.Clear(_masterLeft, 0, n);
		Array.Clear(_masterRight, 0, n);

		var activeVoices = 0;
		var activeOperators = 0;
		foreach (var voice in _voices)
		{
			if (!voice.Active)
				continue;
			var group = voice.Instrument!.Group;
			var ended = voice.Render(n, _scratch, _groupLeft[group], _groupRight[group], _spaceLeft, _spaceRight, _echoLeft, _echoRight, _keyLeft, _keyRight, GainSlots,
				ref _released);
			if (voice.HasPending && voice.Fade <= 0f)
			{
				var pending = voice.Pending;
				var take = voice.PendingTake;
				voice.HasPending = false;
				voice.PendingTake = null;
				if (_instruments[pending.Instrument] is { } instrument)
				{
					var patch = pending.Kind == ChipEventKind.Hit ? instrument.PiecePatches[pending.PieceIndex] : instrument.Patch!;
					Start(voice, instrument, patch, in pending, take);
				}
				else
					voice.Active = false;
			}
			else if (ended && !voice.HasPending)
				voice.Active = false;
			var index = voice.InstrumentIndex;
			if ((uint)index < (uint)InstrumentPeak.Length && voice.LastPeak > InstrumentPeak[index])
				InstrumentPeak[index] = voice.LastPeak;
			if (voice.Active)
			{
				activeVoices++;
				activeOperators += voice.OperatorCount;
			}
		}

		for (var g = 0; g < _groupLeft.Length; g++)
			Mix(_groupEffects[g], _groupLeft[g], _groupRight[g], n);
		Mix(_spaceEffects, _spaceLeft, _spaceRight, n);
		Mix(_echoEffects, _echoLeft, _echoRight, n);
		var masterLeft = _masterLeft.AsSpan(0, n);
		var masterRight = _masterRight.AsSpan(0, n);
		Run(_masterEffects, masterLeft, masterRight);
		masterLeft.CopyTo(left);
		masterRight.CopyTo(right);

		_activeVoices = activeVoices;
		_activeOperators = activeOperators;
		_stolenVoices = _stolen;
	}

	// A bus of the chip's own: its effects run over it, then it is added into the master
	private void Mix(AudioEffect[] effects, float[] left, float[] right, int n)
	{
		var l = left.AsSpan(0, n);
		var r = right.AsSpan(0, n);
		Run(effects, l, r);
		for (var s = 0; s < n; s++)
		{
			_masterLeft[s] += left[s];
			_masterRight[s] += right[s];
		}
	}

	// A chain of effects over a block, each keyed one handed the chip's key first, even if it then rests
	private void Run(AudioEffect[] effects, Span<float> left, Span<float> right)
	{
		var n = left.Length;
		foreach (var effect in effects)
		{
			if (effect is IKeyedEffect keyed)
				keyed.Key(_keyLeft.AsSpan(0, n), _keyRight.AsSpan(0, n));
			if (!effect.Bypass)
				effect.Run(left, right);
		}
	}
}
