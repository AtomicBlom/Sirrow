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

	/// <summary>The song it is a section of.</summary>
	public Song Song { get; }
	/// <summary>Where it stands among its song's sections, from 0 (<see cref="Song.Sections"/>).</summary>
	public int Index { get; }
	internal SectionChords Chords { get; }

	/// <summary>Its chords as written, a bar to each <c>|</c>: "I | IV | V vi | I".</summary>
	public string ChordText => Chords.Text;

	/// <summary>
	/// <paramref name="track"/> plays <paramref name="part"/> through the section: repeated if shorter, cut at the end if
	/// longer. The part is read here, against the song's meter.
	/// </summary>
	/// <exception cref="NotationException">The part's notation has a mistake.</exception>
	/// <exception cref="ArgumentException">The track isn't this song's, or can't play that kind of part.</exception>
	public Placement Play(SongTrack track, Part part)
	{
		Song.CheckOpen();

		ArgumentNullException.ThrowIfNull(track);
		ArgumentNullException.ThrowIfNull(part);
		if (track.Song != Song)
			throw new ArgumentException($"Track '{track.Name}' belongs to another song.", nameof(track));
		part.Check(track);
		var placement = new Placement(this, track, part, part.Parse(Song.Meter));
		_placements.Add(placement);
		return placement;
	}

	private readonly List<(SongTrack Track, Chip.Macro Macro, float From, float To)> _sweeps = [];

	internal IReadOnlyList<(SongTrack Track, Chip.Macro Macro, float From, float To)> Sweeps => _sweeps;

	/// <summary>
	/// While this section plays, one of <paramref name="track"/>'s patch macros moves from <paramref name="from"/> at its
	/// first bar to <paramref name="to"/> at its end, whatever the track holds it at or has it follow elsewhere: a filter
	/// opening over a build, or closing over the bars before a break. In any other section the macro is the track's own.
	/// </summary>
	/// <exception cref="ArgumentException">The track isn't this song's.</exception>
	public void Sweep(SongTrack track, Chip.Macro macro, float from, float to)
	{
		Song.CheckOpen();

		ArgumentNullException.ThrowIfNull(track);
		if (track.Song != Song)
			throw new ArgumentException($"Track '{track.Name}' belongs to another song.", nameof(track));
		if ((uint)macro.Id >= Chip.Macro.Count)
			throw new ArgumentOutOfRangeException(nameof(macro), macro.Id, $"A macro is 0 to {Chip.Macro.Count - 1}.");
		_sweeps.Add((track, macro, from, to));
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
