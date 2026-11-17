namespace Loom.Audio.Synth.Music;

/// <summary>A part a track plays in a section, and when: a condition, a level following a cue, a fill. Its setters chain.</summary>
public sealed class Placement
{
	internal Placement(Section section, SongTrack track, Part part, ParsedPart parsed)
	{
		Section = section;
		Track = track;
		Part = part;
		Parsed = parsed;
		Name = track.Name;
	}

	/// <summary>Its name: the track's, unless <see cref="Named"/>.</summary>
	public string Name { get; private set; }

	internal Section Section { get; }
	internal SongTrack Track { get; }
	internal Part Part { get; }
	internal ParsedPart Parsed { get; }
	internal Condition? Condition { get; private set; }
	internal Cue? LevelCue { get; private set; }
	internal bool OnPhraseValue { get; private set; }
	internal ParsedPart? Fill { get; private set; }

	/// <summary>The same placement under a name of its own, as the workbench shows it: "calm motif".</summary>
	public Placement Named(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		return this;
	}

	/// <summary>It plays only while <paramref name="condition"/> holds.</summary>
	public Placement When(Condition condition)
	{
		ArgumentNullException.ThrowIfNull(condition);
		Condition = condition;
		return this;
	}

	/// <summary>Its volume follows <paramref name="cue"/>, 0 silent to 1 full, eased.</summary>
	public Placement Level(Cue cue)
	{
		ArgumentNullException.ThrowIfNull(cue);
		LevelCue = cue;
		return this;
	}

	/// <summary>It comes and goes on any bar line, as its condition changes. The default.</summary>
	public Placement OnBar()
	{
		OnPhraseValue = false;
		return this;
	}

	/// <summary>It comes in only where its part starts over, and once in, plays that pass out.</summary>
	public Placement OnPhrase()
	{
		OnPhraseValue = true;
		return this;
	}

	/// <summary><paramref name="fill"/> replaces the section's last bar, every pass.</summary>
	public Placement EndWith(Part fill)
	{
		ArgumentNullException.ThrowIfNull(fill);
		fill.Check(Track);
		Fill = fill.Parse(Section.Song.Meter);
		return this;
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
