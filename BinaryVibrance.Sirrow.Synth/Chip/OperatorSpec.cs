using BinaryVibrance.Sirrow.Synth.Dsp;

namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>One operator of a <see cref="Patch"/>: what it makes, at what pitch and level, and what feeds it. Written with a <see cref="PatchBuilder"/>.</summary>
public sealed record OperatorSpec
{
	/// <summary>What it makes.</summary>
	public OperatorKind Kind { get; init; }

	/// <summary>Its frequency as a multiple of the note's.</summary>
	public float Ratio { get; init; } = 1f;

	/// <summary>Above 0, this frequency in hertz whatever the note: a drum's body.</summary>
	public float FixedHz { get; init; }

	/// <summary>Cents off its ratio's pitch: a few thicken it against another operator.</summary>
	public float DetuneCents { get; init; }

	/// <summary>
	/// Where in its cycle a <see cref="OperatorKind.Sine"/> or <see cref="OperatorKind.Wave"/> starts each note, 0 to 1.
	/// Several of the same wave a few cents apart all starting at 0 start as one loud wave, which is a click; started at
	/// different points they are apart from the first sample.
	/// </summary>
	public float StartPhase { get; init; }

	/// <summary>Its level, a linear gain.</summary>
	public float Level { get; init; } = 1f;

	/// <summary>How its level moves over the note.</summary>
	public EnvelopeShape Envelope { get; init; } = EnvelopeShape.Gate;

	/// <summary>How much its level follows the note's velocity: 0 ignores it, 1 scales fully with it.</summary>
	public float Velocity { get; init; }

	/// <summary>A <see cref="OperatorKind.Sine"/> fed back into its own phase, in radians: 0 is pure, more grows toward a saw.</summary>
	public float Feedback { get; init; }

	/// <summary>The operators phase-modulating a <see cref="OperatorKind.Sine"/>, each made before it.</summary>
	public Modulation[] Modulators { get; init; } = [];

	/// <summary>The operators exciting a <see cref="OperatorKind.String"/> or <see cref="OperatorKind.Tube"/>, each made before it.</summary>
	public int[] Drivers { get; init; } = [];

	/// <summary>A <see cref="OperatorKind.Wave"/>'s wave.</summary>
	public Waveform Waveform { get; init; }

	/// <summary>A pulse's width, 0 to 1: 0.5 is a square.</summary>
	public float PulseWidth { get; init; } = 0.5f;

	/// <summary>A <see cref="OperatorKind.Noise"/> made pink: the same energy in every octave, softer than white.</summary>
	public bool Pink { get; init; }

	/// <summary>A <see cref="OperatorKind.Sample"/>'s recordings.</summary>
	public SampleSet? Samples { get; init; }

	/// <summary>A <see cref="OperatorKind.Sample"/> that plays to its end whatever the gate: a drum hit.</summary>
	public bool OneShot { get; init; }

	/// <summary>A <see cref="OperatorKind.String"/>'s ring: seconds to fall 60 dB.</summary>
	public float DecaySeconds { get; init; } = 2f;

	/// <summary>A <see cref="OperatorKind.String"/>'s tone, 0 bright to 1 dull.</summary>
	public float Damping { get; init; } = 0.5f;

	/// <summary>
	/// Where along a <see cref="OperatorKind.String"/> it is plucked, as a share of its length from the bridge, 0.02 to 0.5:
	/// near the bridge is thin and bright, the middle round and hollow. The harmonics with a still point there are missing.
	/// </summary>
	public float Pick { get; init; } = 0.2f;

	/// <summary>A <see cref="OperatorKind.Modes"/> operator's modes, at most <see cref="Mode.Max"/>.</summary>
	public Mode[] Modes { get; init; } = [];

	/// <summary>How its level follows the keyboard, in decibels an octave above middle C (below it, the other way): negative keeps high notes from growing harsh.</summary>
	public float KeyLevelDb { get; init; }

	/// <summary>
	/// How its envelope's decay and release, a string's ring and its modes' follow the keyboard: the share by which each
	/// octave above middle C shortens them (and each below lengthens them), as a struck or plucked note rings shorter the
	/// higher it is. 0 leaves them alone; 0.5 is typical.
	/// </summary>
	public float KeyDecay { get; init; }
}
