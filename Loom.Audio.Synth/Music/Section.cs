namespace Loom.Audio.Synth.Music;

/// <summary>A stretch of a song (a verse, a chorus) some bars long over its chords, the parts its tracks play in it, and
/// where the song goes as it ends (<see cref="Exits"/>).</summary>
public sealed class Section
{
	private readonly List<Placement> _placements = [];
	private readonly List<SectionExit> _exits = [];

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

	/// <summary>Its chords bar by bar, as written and as read. Fewer bars than the section has repeat to fill it.</summary>
	public IReadOnlyList<ChordBar> Progression => Chords.Progression;

	/// <summary>
	/// Its ways out, in the order they are tried as it ends: the first whose condition holds is taken, and the last, made by
	/// <see cref="Then"/>, always holds. A section with none plays again.
	/// </summary>
	public IReadOnlyList<SectionExit> Exits => _exits;

	/// <summary>
	/// As it ends, the song goes on to <paramref name="to"/> if <paramref name="when"/> holds, before any exit added after
	/// this: <c>verse.Exit(chorus, Cues.Intensity.Above(0.7f))</c>, <c>verse.Exit(verse, Pass.Below(2))</c>.
	/// </summary>
	/// <exception cref="InvalidOperationException">An exit that always holds (<see cref="Then"/>) was added already, so none after it could be taken.</exception>
	public void Exit(Section to, Condition when)
	{
		ArgumentNullException.ThrowIfNull(when);
		AddExit(to, when);
	}

	/// <summary>As it ends, if no exit before this held, the song goes on to <paramref name="to"/>: the last exit.</summary>
	/// <exception cref="InvalidOperationException">It has such an exit already.</exception>
	public void Then(Section to) => AddExit(to, null);

	private void AddExit(Section to, Condition? when)
	{
		Song.CheckOpen();

		ArgumentNullException.ThrowIfNull(to);
		if (to.Song != Song)
			throw new ArgumentException($"Section '{to.Name}' belongs to another song.", nameof(to));
		if (_exits.Count > 0 && _exits[^1].When is null)
			throw new InvalidOperationException($"Section '{Name}' always goes on to '{_exits[^1].To.Name}' already: an exit after that could never be taken.");
		_exits.Add(new SectionExit(when, to));
	}

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
