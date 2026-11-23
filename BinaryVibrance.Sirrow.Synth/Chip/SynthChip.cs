namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>
/// A synthesizer to play: instruments from <see cref="Patch"/>es and <see cref="DrumKit"/>s, on a pool of voices, through
/// groups of effects and two shared sends (a room and an echo), as one <see cref="AudioSource"/>.
/// <code>
/// var chip = new SynthChip();
/// var bell = chip.AddInstrument(Patch.Build("bell", p => p.Out(p.Sine(1f, Env.Decay(1.5f)))));
/// Audio.Play(chip, sfx);
/// chip.Play(bell, note: 72, velocity: 0.7f, seconds: 0.5f);
/// </code>
/// </summary>
/// <remarks>
/// Its methods are for the game thread: each checks its arguments, then queues an event the audio thread takes at the
/// start of its next block, so a note is heard within a block of the call. Neither side allocates once it is playing.
/// </remarks>
public sealed class SynthChip : AudioSource
{
	private readonly ChipOptions _options;
	private readonly ChipCore _core;
	private readonly AudioEventQueue<ChipEvent> _events;
	private readonly ChipInstrumentState?[] _instruments;
	private int _instrumentCount;
	private int _nextNote = 1;

	/// <summary>A chip sized and routed by <paramref name="options"/> (<see cref="ChipOptions"/>'s defaults when null).</summary>
	public SynthChip(ChipOptions? options = null)
	{
		_options = options ?? new ChipOptions();
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.Voices, nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.MaxInstruments, nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.EventCapacity, nameof(options));
		if (_options.Groups.Length == 0)
			throw new ArgumentException("A chip has at least one group.", nameof(options));
		_core = new ChipCore(_options);
		_events = new AudioEventQueue<ChipEvent>(_options.EventCapacity);
		_instruments = new ChipInstrumentState?[_options.MaxInstruments];
	}

	/// <summary>How busy it was at its last block.</summary>
	public ChipStats Stats => _core.Stats;

	// The core, for tests that look at its voices
	internal ChipCore Core => _core;

	/// <summary>Adds <paramref name="patch"/> as an instrument, playing through group <paramref name="group"/>.</summary>
	/// <exception cref="InvalidOperationException">The chip already holds <see cref="ChipOptions.MaxInstruments"/>.</exception>
	public ChipInstrument AddInstrument(Patch patch, int group = 0)
	{
		ArgumentNullException.ThrowIfNull(patch);
		return Add(new ChipInstrumentState(patch, CheckGroup(group)));
	}

	/// <summary>Adds <paramref name="kit"/>, playing through group <paramref name="group"/>; play it with <see cref="Hit"/>.</summary>
	/// <exception cref="InvalidOperationException">The chip already holds <see cref="ChipOptions.MaxInstruments"/>.</exception>
	public ChipInstrument AddKit(DrumKit kit, int group = 0)
	{
		ArgumentNullException.ThrowIfNull(kit);
		return Add(new ChipInstrumentState(kit, CheckGroup(group)));
	}

	/// <summary>Starts a note, held until <see cref="NoteOff"/>.</summary>
	/// <param name="instrument">A patch added to this chip.</param>
	/// <param name="note">A MIDI note number, 69 being A 440; fractions bend it.</param>
	/// <param name="velocity">How hard, 0 to 1: the level goes as its square.</param>
	/// <param name="pan">-1 left to 1 right, added to the patch's own.</param>
	public NoteHandle NoteOn(ChipInstrument instrument, float note, float velocity = 0.8f, float pan = 0f)
	{
		var id = _nextNote;
		_nextNote = _nextNote == int.MaxValue ? 1 : _nextNote + 1;
		Post(Note(instrument, note, velocity, pan, id, 0f));
		return new NoteHandle(id);
	}

	/// <summary>Lets a note go: its envelopes release. A note already ended or moved on is ignored.</summary>
	public void NoteOff(NoteHandle note) => Post(new ChipEvent { Kind = ChipEventKind.NoteOff, NoteId = note.Id });

	/// <summary>A note on, let go after <paramref name="seconds"/>: for a sound that needs no handle.</summary>
	public void Play(ChipInstrument instrument, float note, float velocity, float seconds, float pan = 0f)
	{
		if (!float.IsFinite(seconds) || seconds <= 0f)
			throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A note lasts more than zero seconds.");
		var id = _nextNote;
		_nextNote = _nextNote == int.MaxValue ? 1 : _nextNote + 1;
		Post(Note(instrument, note, velocity, pan, id, seconds));
	}

	/// <summary>Strikes a piece of a kit at <paramref name="velocity"/> (0 to 1).</summary>
	/// <exception cref="ArgumentException">The instrument is a patch, or the kit has no such piece.</exception>
	public void Hit(ChipInstrument kit, DrumPiece piece, float velocity = 0.8f)
	{
		var state = Instrument(kit);
		if (state.Kit is null)
			throw new ArgumentException("The instrument is a patch, not a kit: play it with NoteOn or Play.", nameof(kit));
		CheckVelocity(velocity);
		var index = state.Kit.IndexOf(piece);
		if (index < 0)
			throw new ArgumentException($"The kit '{state.Kit.Name}' has no '{piece.Id}'.", nameof(piece));
		Post(new ChipEvent { Kind = ChipEventKind.Hit, Instrument = kit.Index, PieceIndex = index, Velocity = velocity, Note = 60f });
	}

	/// <summary>Sets one of an instrument's macros, 0 to 1: whatever its patch's macro specs say it moves.</summary>
	public void SetMacro(ChipInstrument instrument, Macro macro, float value)
	{
		Instrument(instrument);
		if ((uint)macro.Id >= Macro.Count)
			throw new ArgumentOutOfRangeException(nameof(macro), macro.Id, $"A macro is 0 to {Macro.Count - 1}.");
		if (!float.IsFinite(value))
			throw new ArgumentOutOfRangeException(nameof(value), value, "A macro's value is 0 to 1.");
		Post(new ChipEvent { Kind = ChipEventKind.Macro, Instrument = instrument.Index, Macro = macro.Id, Value = value });
	}

	/// <summary>
	/// Swaps the patch an instrument plays, from the chip's next block: notes already sounding finish on the patch they
	/// started with; its macros, gain and mute carry over. For a tool editing a patch while it plays.
	/// </summary>
	/// <exception cref="ArgumentException">The instrument is a kit.</exception>
	public void Replace(ChipInstrument instrument, Patch patch)
	{
		ArgumentNullException.ThrowIfNull(patch);
		var state = Instrument(instrument);
		if (state.Patch is null)
			throw new ArgumentException("The instrument is a kit, and a kit's patch isn't replaced.", nameof(instrument));
		var next = new ChipInstrumentState(patch, state.Group);
		_instruments[instrument.Index] = next;
		Post(new ChipEvent { Kind = ChipEventKind.ReplaceInstrument, Instrument = instrument.Index, Payload = next });
	}

	/// <summary>Lets every sounding note go.</summary>
	public void AllNotesOff() => Post(new ChipEvent { Kind = ChipEventKind.AllOff });

	/// <inheritdoc/>
	protected override void OnStart(int sampleRate) => _core.Prepare(sampleRate);

	/// <inheritdoc/>
	protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
	{
		while (_events.TryTake(out var e))
			_core.Handle(in e);
		_core.Render(left, right);
		return true;
	}

	private ChipInstrument Add(ChipInstrumentState state)
	{
		if (_instrumentCount == _instruments.Length)
			throw new InvalidOperationException($"The chip already holds its {_instruments.Length} instruments: raise ChipOptions.MaxInstruments for more.");
		var index = _instrumentCount++;
		_instruments[index] = state;
		Post(new ChipEvent { Kind = ChipEventKind.AddInstrument, Instrument = index, Payload = state });
		return new ChipInstrument(index);
	}

	private ChipEvent Note(ChipInstrument instrument, float note, float velocity, float pan, int id, float seconds)
	{
		var state = Instrument(instrument);
		if (state.Patch is null)
			throw new ArgumentException("The instrument is a kit: play it with Hit.", nameof(instrument));
		if (!float.IsFinite(note) || note < 0f || note > 140f)
			throw new ArgumentOutOfRangeException(nameof(note), note, "A note is a MIDI note number, 0 to 140.");
		CheckVelocity(velocity);
		if (!float.IsFinite(pan) || pan < -1f || pan > 1f)
			throw new ArgumentOutOfRangeException(nameof(pan), pan, "A pan runs from -1 (left) to 1 (right).");
		return new ChipEvent { Kind = ChipEventKind.NoteOn, Instrument = instrument.Index, NoteId = id, Note = note, Velocity = velocity, Pan = pan, Seconds = seconds };
	}

	private ChipInstrumentState Instrument(ChipInstrument instrument) =>
		(uint)instrument.Index < (uint)_instrumentCount
			? _instruments[instrument.Index]!
			: throw new ArgumentOutOfRangeException(nameof(instrument), instrument.Index, "No such instrument in this chip.");

	private int CheckGroup(int group) =>
		(uint)group < (uint)_options.Groups.Length
			? group
			: throw new ArgumentOutOfRangeException(nameof(group), group, $"The chip has {_options.Groups.Length} groups.");

	private static void CheckVelocity(float velocity)
	{
		if (!float.IsFinite(velocity) || velocity < 0f || velocity > 1f)
			throw new ArgumentOutOfRangeException(nameof(velocity), velocity, "A velocity is 0 to 1.");
	}

	private void Post(in ChipEvent e)
	{
		if (!_events.TryPost(e))
			throw new InvalidOperationException(
				$"The chip's event queue is full ({_options.EventCapacity} waiting): is it playing? Raise ChipOptions.EventCapacity for more in one frame.");
	}
}
