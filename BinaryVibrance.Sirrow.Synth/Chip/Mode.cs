namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>
/// One way a struck or rung thing vibrates, for a <see cref="OperatorKind.Modes"/> operator: a frequency it rings at, how
/// long, and how strongly. A bar, a bell or a drum's skin is a handful of them at ratios no string has; an instrument's
/// body is a few at fixed frequencies whatever note is played through it.
/// </summary>
/// <param name="Ratio">Its frequency as a multiple of the note's, where <paramref name="Hz"/> is 0.</param>
/// <param name="Hz">Above 0, its frequency in hertz whatever the note: a body's resonance.</param>
/// <param name="DecaySeconds">How long it rings: seconds to fall 60 dB.</param>
/// <param name="Level">How strongly it answers, a linear gain.</param>
public readonly record struct Mode(float Ratio, float Hz, float DecaySeconds, float Level)
{
	/// <summary>The most modes one operator has.</summary>
	public const int Max = 8;

	/// <summary>A mode at <paramref name="ratio"/> times the note's frequency.</summary>
	public static Mode At(float ratio, float decaySeconds, float level = 1f) => new(ratio, 0f, decaySeconds, level);

	/// <summary>A mode at <paramref name="hz"/> whatever the note.</summary>
	public static Mode Fixed(float hz, float decaySeconds, float level = 1f) => new(1f, hz, decaySeconds, level);
}
