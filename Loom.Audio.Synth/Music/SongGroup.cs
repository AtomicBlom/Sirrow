namespace Loom.Audio.Synth.Music;

/// <summary>A group of a song's tracks sharing effects (guitars through a distortion): a chip group (<see cref="Chip.ChipGroup"/>).</summary>
public sealed class SongGroup
{
	internal SongGroup(string name, int index, Func<AudioEffect>[] effects)
	{
		Name = name;
		Index = index;
		Effects = effects;
	}

	/// <summary>Its name.</summary>
	public string Name { get; }

	internal int Index { get; }
	internal Func<AudioEffect>[] Effects { get; }
}
