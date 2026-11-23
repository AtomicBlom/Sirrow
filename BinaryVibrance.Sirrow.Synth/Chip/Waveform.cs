namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>The wave a <see cref="OperatorKind.Wave"/> operator makes.</summary>
public enum Waveform
{
	/// <summary>A sawtooth: every harmonic, bright and buzzy.</summary>
	Saw,
	/// <summary>A pulse of <see cref="OperatorSpec.PulseWidth"/>: a square at 0.5, hollow; thinner as it narrows.</summary>
	Pulse,
}
