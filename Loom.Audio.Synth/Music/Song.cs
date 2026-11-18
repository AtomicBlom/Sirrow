using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// A piece of music as code: its tempo, meter and scale, its tracks (patches and kits, in groups of effects), its
/// sections and the parts played in each, and the order they come in. Build one in a method that makes it anew each
/// time (so hot reload rebuilds it), then hand it to a <see cref="MusicPlayer"/>.
/// <code>
/// var song = new Song("groove") { Tempo = 128, Scale = Scale.Minor(Note.E) };
/// var drums = song.Track("drums", kit);
/// var bass = song.Track("bass", bassPatch).Octave(2);
/// var verse = song.Section("verse", 4, "i | VI | III | VII");
/// verse.Play(drums, Beat.Eighths((Kit.Kick, "x... x..."), (Kit.Snare, "..x. ..x.")));
/// verse.Play(bass, Line.Eighths("1 1 . 1 5, 1 . 1").OverChords());
/// song.Order(verse);
/// </code>
/// </summary>
/// <param name="name">Its name.</param>
public sealed class Song(string name)
{
	private readonly List<SongGroup> _groups = [];
	private readonly List<SongTrack> _tracks = [];
	private readonly List<Section> _sections = [];
	private Section[] _order = [];

	// As many as a chip has instruments
	internal const int MaxTracks = 64;

	/// <summary>Its name.</summary>
	public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

	// Fixed once a player has compiled it: what plays was made from it, and a change after would play nowhere, or past the
	// end of what was compiled. A rebuild makes a new song
	internal bool IsCompiled { get; private set; }

	internal void MarkCompiled() => IsCompiled = true;

	// Every change to the song, its sections, tracks, macros and placements asks this first
	internal void CheckOpen()
	{
		if (IsCompiled)
			throw new InvalidOperationException($"Song '{Name}' has been played, and is fixed: build a new one to change it.");
	}

	/// <summary>Its tempo, in beats (quarter notes) a minute. Default 120.</summary>
	public float Tempo
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = 120f;

	/// <summary>Its time signature. Default 4/4. Set it before placing any part: parts are checked against it.</summary>
	/// <exception cref="InvalidOperationException">A part is already placed, checked against the meter it replaces.</exception>
	public Meter Meter
	{
		get;
		set
		{
			CheckOpen();
			if (_sections.Exists(s => s.Placements.Count > 0))
				throw new InvalidOperationException(
					$"Song '{Name}': its parts were placed against {field}; set the meter before placing any part.");
			field = value;
		}
	} = Meter.FourFour;

	/// <summary>The scale it is written in; a player can play it in another.</summary>
	public Scale Scale
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = Scale.Major(Note.C);

	/// <summary>How many notes sound at once. Default 32.</summary>
	public int Voices
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = 32;

	/// <summary>The space send's effects: <see cref="ChipOptions"/>'s default reverb unless set.</summary>
	public Func<AudioEffect>[] SpaceEffects
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = new ChipOptions().SpaceEffects;

	/// <summary>The echo send's effects: <see cref="ChipOptions"/>'s default delay unless set.</summary>
	public Func<AudioEffect>[] EchoEffects
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = new ChipOptions().EchoEffects;

	/// <summary>Effects over the whole song. Default none.</summary>
	public Func<AudioEffect>[] MasterEffects
	{
		get;
		set
		{
			CheckOpen();
			field = value;
		}
	} = [];

	/// <summary>Its tracks, in the order made.</summary>
	public IReadOnlyList<SongTrack> Tracks => _tracks;

	/// <summary>Its sections, in the order made.</summary>
	public IReadOnlyList<Section> Sections => _sections;

	/// <summary>Its groups, in the order made.</summary>
	public IReadOnlyList<SongGroup> Groups => _groups;

	/// <summary>Every cue a condition, a level or a macro names, each once, in the order first named.</summary>
	public IReadOnlyList<CueBase> Cues
	{
		get
		{
			var cues = new List<CueBase>();
			void Add(CueBase cue)
			{
				if (!cues.Contains(cue))
					cues.Add(cue);
			}
			foreach (var track in _tracks)
				foreach (var macro in track.Macros)
					if (macro.Cue is { } cue)
						Add(cue);
			foreach (var section in _sections)
				foreach (var placement in section.Placements)
				{
					if (placement.Condition is { } condition)
						foreach (var cue in condition.Cues)
							Add(cue);
					if (placement.LevelCue is { } level)
						Add(level);
				}
			return cues;
		}
	}

	internal IReadOnlyList<Section> OrderList => _order;
	internal Func<Conductor, Section>? Conductor { get; private set; }

	/// <summary>A group of tracks sharing <paramref name="effects"/> (<see cref="Effects.Fx"/> makes them).</summary>
	public SongGroup Group(string name, params Func<AudioEffect>[] effects)
	{
		CheckOpen();

		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(effects);
		var group = new SongGroup(name, _groups.Count + 1, effects);
		_groups.Add(group);
		return group;
	}

	/// <summary>A track that plays <paramref name="patch"/>: give it <see cref="Line"/>s and <see cref="Strum"/>s.</summary>
	public SongTrack Track(string name, Patch patch, SongGroup? group = null)
	{
		CheckOpen();

		ArgumentNullException.ThrowIfNull(patch);
		return AddTrack(name, patch, null, group);
	}

	/// <summary>A track that plays <paramref name="kit"/>: give it <see cref="Beat"/>s.</summary>
	public SongTrack Track(string name, DrumKit kit, SongGroup? group = null)
	{
		CheckOpen();

		ArgumentNullException.ThrowIfNull(kit);
		return AddTrack(name, null, kit, group);
	}

	private SongTrack AddTrack(string name, Patch? patch, DrumKit? kit, SongGroup? group)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (group is not null && !_groups.Contains(group))
			throw new ArgumentException($"Group '{group.Name}' belongs to another song.", nameof(group));
		if (_tracks.Count == MaxTracks)
			throw new InvalidOperationException($"A song has at most {MaxTracks} tracks.");
		var track = new SongTrack(this, name, _tracks.Count, patch, kit, group);
		_tracks.Add(track);
		return track;
	}

	/// <summary>
	/// A section <paramref name="bars"/> long over <paramref name="chords"/>: bars separated by <c>|</c>, each one chord or
	/// two or four sharing the bar (<see cref="Chord.Parse"/>), repeated to fill the section if fewer.
	/// </summary>
	/// <exception cref="NotationException">The chords have a mistake.</exception>
	public Section Section(string name, int bars, string chords = "I")
	{
		CheckOpen();

		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bars);
		var section = new Section(this, name, _sections.Count, bars, new SectionChords(chords));
		_sections.Add(section);
		return section;
	}

	/// <summary>The sections played in turn, then from the first again.</summary>
	public void Order(params Section[] sections)
	{
		CheckOpen();

		ArgumentNullException.ThrowIfNull(sections);
		foreach (var section in sections)
			if (section.Song != this)
				throw new ArgumentException($"Section '{section.Name}' belongs to another song.", nameof(sections));
		_order = [.. sections];
		Conductor = null;
	}

	/// <summary>The next section chosen in code, near the end of each section, from the cues and what is playing.</summary>
	public void Conduct(Func<Conductor, Section> next)
	{
		CheckOpen();

		ArgumentNullException.ThrowIfNull(next);
		Conductor = next;
		_order = [];
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
