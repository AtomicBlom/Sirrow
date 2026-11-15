namespace Loom.Audio.Synth.Chip;

/// <summary>Which output of a patch's filter (a state-variable filter) a voice is heard through.</summary>
public enum FilterKind
{
	/// <summary>Lets through what is below the cutoff.</summary>
	LowPass,
	/// <summary>Lets through what is near the cutoff.</summary>
	BandPass,
	/// <summary>Lets through what is above the cutoff.</summary>
	HighPass,
}
