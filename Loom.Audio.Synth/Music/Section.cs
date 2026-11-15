namespace Loom.Audio.Synth.Music;

/// <summary>A stretch of a song (a verse, a chorus) some bars long over its chords, and the parts its tracks play in it.</summary>
public sealed class Section
{
	private readonly List<Placement> _placements = [];

	internal Section(Song song, string name, int index, int bars, SectionChords chords)
	{
		Song = song;
		Name = name;
		Index = index;
		Bars = bars;
		Chords = chords;
	}

	/// <summary>Its name.</summary>
	public string Name { get; }

	/// <summary>How many bars long it is.</summary>
	public int Bars { get; }

	/// <summary>The parts played in it.</summary>
	public IReadOnlyList<Placement> Placements => _placements;

	internal Song Song { get; }
	internal int Index { get; }
	internal SectionChords Chords { get; }

	/// <summary>
	/// <paramref name="track"/> plays <paramref name="part"/> through the section: repeated if shorter, cut at the end if
	/// longer. The part is read here, against the song's meter.
	/// </summary>
	/// <exception cref="NotationException">The part's notation has a mistake.</exception>
	/// <exception cref="ArgumentException">The track isn't this song's, or can't play that kind of part.</exception>
	public Placement Play(SongTrack track, Part part)
	{
		ArgumentNullException.ThrowIfNull(track);
		ArgumentNullException.ThrowIfNull(part);
		if (track.Song != Song)
			throw new ArgumentException($"Track '{track.Name}' belongs to another song.", nameof(track));
		part.Check(track);
		var placement = new Placement(this, track, part, part.Parse(Song.Meter));
		_placements.Add(placement);
		return placement;
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
