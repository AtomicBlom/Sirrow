using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// Plays songs on a bus: one at a time, a new one crossfading over the last. The game tells it cues, and it plays the
/// song's parts as they say; a rebuilt song is swapped in at the next bar line without a break.
/// <code>
/// var music = new MusicPlayer(Audio, musicBus);
/// music.Play(RacePark.Build());
/// music.Set(Cues.Intensity, speed / topSpeed);   // each frame
/// music.Update();                                // each frame
/// </code>
/// </summary>
/// <remarks>Every call is for the thread that made the engine. The song is compiled on that thread; the audio thread only reads what it made.</remarks>
public sealed class MusicPlayer : IDisposable
{
	private readonly AudioEngine _audio;
	private readonly AudioBus _bus;
	private readonly Dictionary<CueBase, float> _cues = [];
	private readonly List<MusicSource> _fading = [];
	private readonly Conductor _conductor;
	private MusicSource? _source;
	private SongState? _swapping;   // a swap posted but not yet known taken
	private int _asked = -1;

	/// <summary>A player for <paramref name="audio"/>, its songs on <paramref name="bus"/>.</summary>
	public MusicPlayer(AudioEngine audio, AudioBus bus)
	{
		ArgumentNullException.ThrowIfNull(audio);
		_audio = audio;
		_bus = bus;
		_conductor = new Conductor(CueValue);
	}

	/// <summary>Whether a song is playing.</summary>
	public bool IsPlaying => _source is not null;

	/// <summary>The song playing (the last swapped in), or null.</summary>
	public Song? Song => Current?.Song.Song;

	/// <summary>The scale it plays in.</summary>
	public Scale Scale => Current?.Song.Scale ?? Scale.Major(Note.C);

	/// <summary>Its tempo, in beats a minute.</summary>
	public float Tempo => Current?.Song.Tempo ?? 0f;

	/// <summary>How long a bar of it lasts, in seconds.</summary>
	public double BarSeconds => Current is { } state
		? state.Song.Song.Meter.Beats * 4.0 / state.Song.Song.Meter.Unit * 60.0 / state.Song.Tempo
		: 0.0;

	/// <summary>Where the song is, as the audio thread last said (a block old at most).</summary>
	public MusicPosition Position
	{
		get
		{
			if (_source is not { } source)
				return default;
			var song = source.State.Song;
			var section = Math.Min(source.SectionIndex, song.Sections.Length - 1);
			return new MusicPosition(song.Sections[section].Name, section, source.Bar, source.Beat, source.AbsoluteBar);
		}
	}

	/// <summary>How busy its chip was at its last block.</summary>
	public ChipStats Stats => _source?.Stats ?? default;

	// The state the game's calls go to: a swap posted, else the one playing
	private SongState? Current => _swapping ?? _source?.State;

	/// <summary>
	/// Plays <paramref name="song"/>, in <paramref name="scale"/> or its own, from its first section. Anything playing fades
	/// out over <paramref name="fadeSeconds"/> as it starts. Every cue the player has been told is in force from the first bar.
	/// </summary>
	public void Play(Song song, Scale? scale = null, float fadeSeconds = 1f)
	{
		ArgumentNullException.ThrowIfNull(song);
		var state = new SongState(CompiledSong.Compile(song, scale ?? song.Scale));
		Tell(state);
		Stop(fadeSeconds);
		_source = new MusicSource(state);
		_swapping = null;
		_asked = -1;
		_audio.Play(_source, _bus);
	}

	/// <summary>
	/// Swaps in <paramref name="song"/> (usually the same song rebuilt from changed code) at the next bar line, in the scale
	/// playing: it carries on in the section of the same name at the same bar, or the first section if that name is gone,
	/// at the new tempo.
	/// </summary>
	public void Swap(Song song)
	{
		ArgumentNullException.ThrowIfNull(song);
		if (_source is not { } source)
		{
			Play(song);
			return;
		}
		var current = Current!;
		var state = new SongState(CompiledSong.Compile(song, current.Song.Scale));
		var map = new int[current.Song.Sections.Length];
		for (var s = 0; s < map.Length; s++)
			map[s] = Math.Max(0, Array.FindIndex(state.Song.Sections, n => n.Name == current.Song.Sections[s].Name));
		state.SectionMap = map;
		Tell(state);
		_swapping = state;
		source.Post(new MusicCommand { Kind = MusicCommandKind.Swap, Payload = state });
	}

	/// <summary>Stops the song, fading out over <paramref name="fadeSeconds"/>.</summary>
	public void Stop(float fadeSeconds = 1f)
	{
		if (_source is not { } source)
			return;
		_audio.Stop(source, fadeSeconds);
		_fading.Add(source);
		_source = null;
		_swapping = null;
	}

	/// <summary>
	/// Once a frame: asks a conducted song's function for its next section near the end of each section, and lets go of a
	/// song that has faded out.
	/// </summary>
	/// <exception cref="InvalidOperationException">The song's conduct function threw (the inner exception); the section loops meanwhile.</exception>
	public void Update()
	{
		for (var i = _fading.Count - 1; i >= 0; i--)
			if (!_fading[i].IsPlaying)
				_fading.RemoveAt(i);
		if (_source is not { } source)
			return;
		if (_swapping is not null && ReferenceEquals(source.State, _swapping))
			_swapping = null;
		var song = source.State.Song;
		if (!song.Conducted || _swapping is not null)
			return;
		if (source.TicksLeft > song.TicksPerBeat || source.SectionStarts == _asked)
			return;
		_asked = source.SectionStarts;
		var current = song.Sections[Math.Min(source.SectionIndex, song.Sections.Length - 1)];
		_conductor.Current = current.Source;
		_conductor.Repeats = source.Pass;
		Section next;
		try
		{
			next = song.Song.Conductor!(_conductor);
		}
		catch (Exception ex)
		{
			source.Post(new MusicCommand { Kind = MusicCommandKind.Next, A = current.Source.Index });
			throw new InvalidOperationException($"Song '{song.Song.Name}': its conduct function threw, and the section loops: {ex.Message}", ex);
		}
		if (next is null || next.Song != song.Song)
			throw new InvalidOperationException($"Song '{song.Song.Name}': its conduct function gave a section that isn't the song's.");
		source.Post(new MusicCommand { Kind = MusicCommandKind.Next, A = next.Index });
	}

	/// <summary>Tells the music a level cue's value. One the song playing doesn't use is remembered for the next.</summary>
	public void Set(Cue cue, float value)
	{
		ArgumentNullException.ThrowIfNull(cue);
		Write(cue, value);
	}

	/// <summary>Tells the music a choice cue's value.</summary>
	public void Set<T>(Cue<T> cue, T value) where T : struct, Enum
	{
		ArgumentNullException.ThrowIfNull(cue);
		Write(cue, Cue<T>.ToValue(value));
	}

	/// <summary>A level cue's value as last told, or its initial value.</summary>
	public float Get(Cue cue) => CueValue(cue);

	private float CueValue(CueBase cue) => _cues.TryGetValue(cue, out var value) ? value : cue.InitialValue;

	private void Write(CueBase cue, float value)
	{
		_cues[cue] = value;
		if (_source?.State is { } playing)
			WriteSlot(playing, cue, value);
		if (_swapping is { } swapping)
			WriteSlot(swapping, cue, value);
	}

	private static void WriteSlot(SongState state, CueBase cue, float value)
	{
		var slot = state.Song.SlotOf(cue);
		if (slot >= 0)
			MusicSource.SetCue(state, slot, value);
	}

	// Every cue told so far, into a song about to play
	private void Tell(SongState state)
	{
		foreach (var (cue, value) in _cues)
			WriteSlot(state, cue, value);
	}

	/// <summary>Goes to <paramref name="section"/> at the next bar line.</summary>
	public void Jump(Section section) => Post(MusicCommandKind.Jump, CheckSection(section).Index);

	/// <summary>Holds the section playing (true) or lets the song go on (false).</summary>
	public void LoopSection(bool loop) => Post(MusicCommandKind.Loop, 0, value: loop ? 1f : 0f);

	/// <summary>Silences a track's new notes, or lets them sound again.</summary>
	public void Mute(SongTrack track, bool muted) => Post(MusicCommandKind.Mute, CheckTrack(track).Index, value: muted ? 1f : 0f);

	/// <summary>Silences every track not soloed while any is.</summary>
	public void Solo(SongTrack track, bool solo) => Post(MusicCommandKind.Solo, CheckTrack(track).Index, value: solo ? 1f : 0f);

	/// <summary>A track's gain over its level, in decibels: 0 as written.</summary>
	public void SetTrackGain(SongTrack track, float db) =>
		Post(MusicCommandKind.TrackGain, CheckTrack(track).Index, value: Dsp.Units.DbToGain(db));

	/// <summary>Holds a placement on or off whatever its condition, from the next bar line (or its next phrase), or back to <see cref="PlacementState.Auto"/>.</summary>
	public void Force(Placement placement, PlacementState state)
	{
		var (section, index) = Find(placement);
		Post(MusicCommandKind.Force, section, index, (int)state);
	}

	/// <summary>Whether a placement is playing now: its section is, and it is on.</summary>
	public bool IsActive(Placement placement)
	{
		var (section, index) = Find(placement);
		var source = _source!;
		return source.SectionIndex == section && index < source.State.Published.Length && source.State.Published[index];
	}

	/// <summary>The loudest a track's notes were in the last block, a linear level: for a meter.</summary>
	public float TrackPeak(SongTrack track)
	{
		var index = CheckTrack(track).Index;
		var peaks = _source!.State.Peaks;
		return index < peaks.Length ? Volatile.Read(ref peaks[index]) : 0f;
	}

	private void Post(MusicCommandKind kind, int a, int b = 0, float value = 0f)
	{
		if (_source is not { } source)
			throw new InvalidOperationException("No song is playing.");
		source.Post(new MusicCommand { Kind = kind, A = a, B = b, Value = value });
	}

	private Song Playing => Song ?? throw new InvalidOperationException("No song is playing.");

	private SongTrack CheckTrack(SongTrack track)
	{
		ArgumentNullException.ThrowIfNull(track);
		if (track.Song != Playing)
			throw new ArgumentException($"Track '{track.Name}' isn't the playing song's.", nameof(track));
		return track;
	}

	private Section CheckSection(Section section)
	{
		ArgumentNullException.ThrowIfNull(section);
		if (section.Song != Playing)
			throw new ArgumentException($"Section '{section.Name}' isn't the playing song's.", nameof(section));
		return section;
	}

	private (int Section, int Index) Find(Placement placement)
	{
		ArgumentNullException.ThrowIfNull(placement);
		var section = CheckSection(placement.Section);
		var index = -1;
		for (var i = 0; i < section.Placements.Count; i++)
			if (ReferenceEquals(section.Placements[i], placement))
				index = i;
		if (index < 0)
			throw new ArgumentException($"Placement '{placement.Name}' isn't the playing song's.", nameof(placement));
		return (section.Index, index);
	}

	/// <summary>Stops everything at once.</summary>
	public void Dispose()
	{
		Stop(0f);
		foreach (var source in _fading)
			_audio.Stop(source);
		_fading.Clear();
	}
}
