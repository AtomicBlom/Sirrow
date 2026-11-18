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

	/// <summary>The section it is placed in.</summary>
	public Section Section { get; }
	/// <summary>The track that plays it.</summary>
	public SongTrack Track { get; }
	internal Part Part { get; }
	internal ParsedPart Parsed { get; }
	/// <summary>When it plays (<see cref="When"/>), or null for always. Its <c>ToString</c> says it in words.</summary>
	public Condition? Condition { get; private set; }
	/// <summary>The cue its level follows (<see cref="Level"/>), or null for its own level.</summary>
	public Cue? LevelCue { get; private set; }
	/// <summary>Whether it comes and goes only where its part starts over (<see cref="OnPhrase"/>), not at any bar line.</summary>
	public bool IsOnPhrase { get; private set; }
	internal ParsedPart? Fill { get; private set; }

	/// <summary>The same placement under a name of its own, as the workbench shows it: "calm motif".</summary>
	public Placement Named(string name)
	{
		Track.Song.CheckOpen();

		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		return this;
	}

	/// <summary>It plays only while <paramref name="condition"/> holds.</summary>
	public Placement When(Condition condition)
	{
		Track.Song.CheckOpen();

		ArgumentNullException.ThrowIfNull(condition);
		Condition = condition;
		return this;
	}

	/// <summary>Its volume follows <paramref name="cue"/>, 0 silent to 1 full, eased.</summary>
	public Placement Level(Cue cue)
	{
		Track.Song.CheckOpen();

		ArgumentNullException.ThrowIfNull(cue);
		LevelCue = cue;
		return this;
	}

	/// <summary>It comes and goes on any bar line, as its condition changes. The default.</summary>
	public Placement OnBar()
	{
		Track.Song.CheckOpen();

		IsOnPhrase = false;
		return this;
	}

	/// <summary>It comes in only where its part starts over, and once in, plays that pass out.</summary>
	public Placement OnPhrase()
	{
		Track.Song.CheckOpen();

		IsOnPhrase = true;
		return this;
	}

	/// <summary><paramref name="fill"/> replaces the section's last bar, every pass.</summary>
	public Placement EndWith(Part fill)
	{
		Track.Song.CheckOpen();

		ArgumentNullException.ThrowIfNull(fill);
		fill.Check(Track);
		Fill = fill.Parse(Section.Song.Meter);
		return this;
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
