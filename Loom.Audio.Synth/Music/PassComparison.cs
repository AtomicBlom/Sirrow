namespace Loom.Audio.Synth.Music;

/// <summary>How a <see cref="PassCondition"/> compares the section's pass with its count.</summary>
public enum PassComparison
{
	/// <summary>More than the count.</summary>
	Above,
	/// <summary>Fewer than the count.</summary>
	Below,
	/// <summary>Exactly the count.</summary>
	Is,
}
