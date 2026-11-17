using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Chip;

/// <summary>
/// Writes a <see cref="Patch"/>, handed to <see cref="Patch.Build"/>'s action. Each operator method adds an operator and
/// returns it (an <see cref="Op"/>) to route; the rest set what the voice does after its operators.
/// </summary>
/// <remarks>
/// The rules, checked when the action returns: at most 16 operators; at least one heard (<see cref="Out"/>); an operator
/// is modulated or driven only by one made before it, in this builder; only a <see cref="OperatorKind.Sine"/> takes
/// modulators or feedback, and only a <see cref="OperatorKind.String"/>, a <see cref="OperatorKind.Tube"/> or
/// <see cref="OperatorKind.Modes"/> drivers; at most two strings and one tube; a tube and modes have a driver, and
/// modes number one to <see cref="Mode.Max"/>; a sample has samples.
/// </remarks>
public sealed class PatchBuilder
{
	private const int MaxStrings = 2;
	private const int MaxTubes = 1;

	private readonly string _name;
	private readonly List<Op> _operators = [];
	private readonly List<OpTap> _outputs = [];
	private readonly List<(Macro Macro, MacroTarget Target, float From, float To, Op? Op)> _macros = [];
	private Patch _settings;

	internal PatchBuilder(string name)
	{
		_name = name;
		_settings = new Patch { Name = name };
	}

	/// <summary>A sine at <paramref name="ratio"/> times the note's frequency.</summary>
	public Op Sine(float ratio, EnvelopeShape envelope, float level = 1f, float velocity = 0f) =>
		Add(new OperatorSpec { Kind = OperatorKind.Sine, Ratio = ratio, Envelope = envelope, Level = level, Velocity = velocity });

	/// <summary>A band-limited saw or pulse at <paramref name="ratio"/> times the note's frequency.</summary>
	public Op Wave(Waveform waveform, float ratio, EnvelopeShape envelope, float level = 1f, float velocity = 0f, float pulseWidth = 0.5f) =>
		Add(new OperatorSpec { Kind = OperatorKind.Wave, Waveform = waveform, Ratio = ratio, Envelope = envelope, Level = level, Velocity = velocity, PulseWidth = pulseWidth });

	/// <summary>White noise, or pink.</summary>
	public Op Noise(EnvelopeShape envelope, float level = 1f, float velocity = 0f, bool pink = false) =>
		Add(new OperatorSpec { Kind = OperatorKind.Noise, Envelope = envelope, Level = level, Velocity = velocity, Pink = pink });

	/// <summary>The envelope itself as a level: a breath's pressure to drive a tube.</summary>
	public Op Level(EnvelopeShape envelope, float level = 1f, float velocity = 0f) =>
		Add(new OperatorSpec { Kind = OperatorKind.Level, Envelope = envelope, Level = level, Velocity = velocity });

	/// <summary>A recording, pitched to the note from its zone's root. One-shot plays to its end whatever the gate.</summary>
	public Op Sample(SampleSet samples, float level = 1f, float velocity = 1f, bool oneShot = false) =>
		Add(new OperatorSpec { Kind = OperatorKind.Sample, Samples = samples, Level = level, Velocity = velocity, OneShot = oneShot });

	/// <summary>
	/// A plucked string: rings for <paramref name="decaySeconds"/>, bright at <paramref name="damping"/> 0 and dull at 1,
	/// plucked <paramref name="pick"/> of the way along it from the bridge (0.02 to 0.5): near the bridge is thin and
	/// bright, the middle round and hollow, a fifth of the way a guitar's usual place.
	/// </summary>
	public Op String(float ratio = 1f, float decaySeconds = 2f, float damping = 0.5f, float pick = 0.2f) =>
		Add(new OperatorSpec { Kind = OperatorKind.String, Ratio = ratio, DecaySeconds = decaySeconds, Damping = damping, Pick = pick });

	/// <summary>A flute's bore, blown by its drivers: a <see cref="Level"/> for the breath and a quiet <see cref="Noise"/>.</summary>
	public Op Tube(float ratio = 1f) =>
		Add(new OperatorSpec { Kind = OperatorKind.Tube, Ratio = ratio });

	/// <summary>
	/// A struck or rung body: its drivers ring a bank of <paramref name="modes"/>, each at its own frequency for its own
	/// time. Drive it with a short burst of noise for a mallet on a bar or a stick on a skin, or with a string for the
	/// body of the instrument it is strung on.
	/// </summary>
	public Op Modes(params Mode[] modes)
	{
		ArgumentNullException.ThrowIfNull(modes);
		return Add(new OperatorSpec { Kind = OperatorKind.Modes, Modes = [.. modes] });
	}

	/// <summary>Hears these operators in the output: an <see cref="Op"/> at level 1, or <see cref="Op.At"/> a level.</summary>
	public void Out(params OpTap[] taps)
	{
		ArgumentNullException.ThrowIfNull(taps);
		_outputs.AddRange(taps);
	}

	/// <summary>Puts a filter after the operators (<see cref="Chip.Filter"/> makes one).</summary>
	public void Filter(FilterSpec filter) => _settings = _settings with { Filter = filter };

	/// <summary>Drives the voice into a soft clip, 0 to 1.</summary>
	public void Drive(float amount) => _settings = _settings with { Drive = amount };

	/// <summary>The voice's gain, in decibels.</summary>
	public void Gain(float db) => _settings = _settings with { GainDb = db };

	/// <summary>Where it sits, -1 left to 1 right.</summary>
	public void Pan(float pan) => _settings = _settings with { Pan = pan };

	/// <summary>Sends <paramref name="level"/> of the voice to one of the chip's shared buses: the room, the echo, or the key keyed effects listen to.</summary>
	public void Send(ChipSend send, float level) => _settings = send switch
	{
		ChipSend.Space => _settings with { SendSpace = level },
		ChipSend.Echo => _settings with { SendEcho = level },
		ChipSend.Key => _settings with { SendKey = level },
		_ => throw new ArgumentOutOfRangeException(nameof(send), send, "A chip sends to its space, its echo or its key."),
	};

	/// <summary>A vibrato of <paramref name="depth"/> semitones at <paramref name="rate"/> hertz, starting <paramref name="delay"/> seconds into a note.</summary>
	public void Vibrato(float rate, float depth, float delay = 0f) =>
		_settings = _settings with { VibratoHz = rate, VibratoSemitones = depth, VibratoDelaySeconds = delay };

	/// <summary>A tremolo: the voice's level dips by up to <paramref name="depth"/> (0 to 1) of itself, <paramref name="rate"/> times a second.</summary>
	public void Tremolo(float rate, float depth) =>
		_settings = _settings with { TremoloHz = rate, TremoloDepth = depth };

	/// <summary>
	/// Each note starts <paramref name="semitones"/> up and falls to pitch, 60 dB of the way in <paramref name="seconds"/>:
	/// a drum's thump. It bends every operator, those at a <see cref="Op.Fixed"/> frequency and samples too.
	/// </summary>
	public void PitchDrop(float semitones, float seconds) =>
		_settings = _settings with { PitchDropSemitones = semitones, PitchDropSeconds = seconds };

	/// <summary>One voice: a new note moves it, sliding over <paramref name="glideSeconds"/> (a time constant).</summary>
	public void Mono(float glideSeconds = 0f) => _settings = _settings with { Mono = true, GlideSeconds = glideSeconds };

	/// <summary>As <paramref name="macro"/> moves from 0 to 1, moves <paramref name="target"/> from <paramref name="from"/> to <paramref name="to"/>; <paramref name="op"/> for an operator's level.</summary>
	public void Macro(Macro macro, MacroTarget target, float from, float to, Op? op = null) => _macros.Add((macro, target, from, to, op));

	private Op Add(OperatorSpec spec)
	{
		var op = new Op(this, _operators.Count, spec);
		_operators.Add(op);
		return op;
	}

	internal Patch Finish()
	{
		if (_operators.Count > Patch.MaxOperators)
			Fail($"it has {_operators.Count} operators, and a patch has at most {Patch.MaxOperators}.");
		if (_outputs.Count == 0)
			Fail("nothing is heard: give Out the operators to hear.");
		if (_outputs.Count > Patch.MaxOutputs)
			Fail($"{_outputs.Count} operators are heard, and a patch hears at most {Patch.MaxOutputs}.");
		var wide = false;

		var strings = 0;
		var tubes = 0;
		var operators = new OperatorSpec[_operators.Count];
		foreach (var op in _operators)
		{
			var spec = op.Spec;
			if (op.Modulators.Count > 0 && spec.Kind != OperatorKind.Sine)
				Fail($"operator {op.Index} is a {spec.Kind}, and only a Sine is modulated.");
			if (spec.Feedback != 0f && spec.Kind != OperatorKind.Sine)
				Fail($"operator {op.Index} is a {spec.Kind}, and only a Sine has feedback.");
			if (op.Drivers.Count > 0 && spec.Kind is not (OperatorKind.String or OperatorKind.Tube or OperatorKind.Modes))
				Fail($"operator {op.Index} is a {spec.Kind}, and only a String, a Tube or Modes are driven.");
			if (spec.Kind == OperatorKind.Modes)
			{
				if (op.Drivers.Count == 0)
					Fail($"operator {op.Index} is Modes with nothing ringing them: drive them with a short Noise, or a String.");
				if (spec.Modes.Length is 0 or > Mode.Max)
					Fail($"operator {op.Index} has {spec.Modes.Length} modes, and Modes have 1 to {Mode.Max}.");
			}
			if (spec.Kind == OperatorKind.String)
				strings++;
			if (spec.Kind == OperatorKind.Tube)
			{
				tubes++;
				if (op.Drivers.Count == 0)
					Fail($"operator {op.Index} is a Tube with nothing blowing it: drive it with a Level (and a quiet Noise).");
			}
			if (spec.Kind == OperatorKind.Sample && (spec.Samples is null || spec.Samples.Zones.Count == 0))
				Fail($"operator {op.Index} is a Sample with no samples.");

			var modulators = new Modulation[op.Modulators.Count];
			for (var i = 0; i < modulators.Length; i++)
				modulators[i] = new Modulation(Before(op.Modulators[i].Op, op, "modulated"), op.Modulators[i].Index);
			var drivers = new int[op.Drivers.Count];
			for (var i = 0; i < drivers.Length; i++)
				drivers[i] = Before(op.Drivers[i], op, "driven");
			operators[op.Index] = spec with { Modulators = modulators, Drivers = drivers };
		}
		if (strings > MaxStrings)
			Fail($"it has {strings} strings, and a patch has at most {MaxStrings}.");
		if (tubes > MaxTubes)
			Fail($"it has {tubes} tubes, and a patch has at most {MaxTubes}.");

		var outputs = new OutputTap[_outputs.Count];
		for (var i = 0; i < outputs.Length; i++)
		{
			if (!float.IsFinite(_outputs[i].Pan) || _outputs[i].Pan < -1f || _outputs[i].Pan > 1f)
				Fail($"an operator is heard at a pan of {_outputs[i].Pan}, and a pan runs from -1 (left) to 1 (right).");
			outputs[i] = new OutputTap(Mine(_outputs[i].Op, "heard"), _outputs[i].Level, _outputs[i].Pan);
			wide |= _outputs[i].Pan != 0f;
		}

		var macros = new MacroSpec[_macros.Count];
		for (var i = 0; i < macros.Length; i++)
		{
			var (macro, target, from, to, op) = _macros[i];
			if ((uint)macro.Id >= Chip.Macro.Count)
				Fail($"macro {macro.Id} is not one of an instrument's {Chip.Macro.Count} (0 to {Chip.Macro.Count - 1}).");
			if (target == MacroTarget.OperatorLevel && op is null)
				Fail($"macro {macro.Id} turns an operator's level, and names no operator.");
			macros[i] = new MacroSpec(macro, target, from, to, op is null ? -1 : Mine(op, "turned by a macro"));
		}

		return _settings with { Operators = operators, Outputs = outputs, Macros = macros, Wide = wide };
	}

	// The index of an operator feeding another: one of this builder's, made before it
	private int Before(Op feeder, Op fed, string verb)
	{
		var index = Mine(feeder, verb);
		if (index >= fed.Index)
			Fail($"operator {fed.Index} is {verb} by operator {index}: an operator is modulated or driven only by one made before it.");
		return index;
	}

	private int Mine(Op? op, string verb)
	{
		if (op is null || op.Owner != this)
			Fail($"an operator {verb} is not one of this patch's own.");
		return op!.Index;
	}

	private void Fail(string message) => throw new PatchException(_name, message);
}
