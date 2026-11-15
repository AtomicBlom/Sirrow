namespace Loom.Audio.Synth.Music;

/// <summary>
/// Something the game tells the music, a <see cref="Cue"/> (a level) or a <see cref="Cue{T}"/> (a choice). A cue is
/// known by its object, not its name: a game keeps its cues in static fields, and every song uses those.
/// </summary>
public abstract class CueBase
{
	private protected CueBase(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
	}

	/// <summary>Its name, for the workbench and messages.</summary>
	public string Name { get; }

	// Its value before the game sets one, as the sequencer holds it
	internal abstract float InitialValue { get; }

	// A choice's options, by name, and each one's value as the sequencer holds it; none for a level
	internal virtual string[] OptionNames => [];
	internal virtual float OptionValue(int index) => index;

	/// <inheritdoc/>
	public override string ToString() => Name;
}
