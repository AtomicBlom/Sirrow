using BinaryVibrance.Sirrow.Synth.Chip;

namespace HelloSynth;

/// <summary>The macros a song turns its patches by: a patch's file names one by its number, a song's by its name ("bright").</summary>
public static class Macros
{
	/// <summary>Opens the saw lead's filter.</summary>
	public static Macro Bright { get; } = new(0);
}
