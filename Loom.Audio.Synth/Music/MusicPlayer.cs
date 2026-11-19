using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// Plays songs on a bus: one at a time, a new one crossfading over the last. The game tells it cues, and it plays the
/// song's parts as they say; a rebuilt song is swapped in at the next bar line without a break.
/// <code>
/// var music = new MusicPlayer(Audio, musicBus);
/// music.Play(Groove.Build());
/// music.Set(Groove.Energy, energy);   // each frame, 0 to 1
/// music.Update();                     // each frame
/// </code>
/// </summary>
/// <remarks>
/// Every call is for the thread that made the engine but <see cref="Prepare"/>, which compiles a song and makes its chip on
/// any thread, for a game that starts its songs without stalling a frame. The audio thread only reads what was made.
/// </remarks>
public sealed class MusicPlayer : IDisposable
{
	private readonly AudioEngine _audio;
	private readonly AudioBus _bus;
	private readonly Dictionary<CueBase, float> _cues = [];
	private readonly Dictionary<string, bool> _muted = [];
	private readonly Dictionary<string, bool> _soloed = [];
	private readonly Dictionary<string, float> _gainDb = [];
	private readonly Dictionary<(string Section, string Placement), PlacementState> _forced = [];
	private readonly List<AudioSource> _fading = [];
	private readonly double _aheadSeconds;
	private MusicSource? _source;
	private AudioSource? _played;   // what the engine plays: the song's source, or an AheadSource wrapping it
	private SongState? _swapping;   // a swap posted but not yet known taken

	/// <summary>
	/// A player for <paramref name="audio"/>, its songs on <paramref name="bus"/>. With <paramref name="aheadSeconds"/> above
	/// 0, each song is rendered that far ahead of the speaker, off the audio thread (<see cref="AheadSource"/>): the audio
	/// thread then only copies it, and the cues and commands are heard up to that much later.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">A lead below 0, or above a second.</exception>
	public MusicPlayer(AudioEngine audio, AudioBus bus, double aheadSeconds = 0)
	{
		ArgumentNullException.ThrowIfNull(audio);
		if (!double.IsFinite(aheadSeconds) || aheadSeconds < 0 || aheadSeconds > 1)
			throw new ArgumentOutOfRangeException(nameof(aheadSeconds), aheadSeconds, "A lead is 0 (live) to a second.");
		_audio = audio;
		_bus = bus;
		_aheadSeconds = aheadSeconds;
	}

	/// <summary>How far ahead of the speaker songs are rendered, in seconds: 0 renders them live, on the audio thread.</summary>
	public double AheadSeconds => _aheadSeconds;

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

	/// <summary>
	/// Where the song is, as its rendering last said (a block old at most). Rendered ahead (<see cref="AheadSeconds"/>), it is
	/// up to the lead ahead of what is heard.
	/// </summary>
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

	/// <summary>
	/// The tick within the section playing (<see cref="Ticks.PerBeat"/> to a beat), as the rendering last said; -1 with
	/// nothing playing. For a playhead over a <see cref="Pattern"/>.
	/// </summary>
	public int SectionTick => _source is { } source
		? source.State.Song.Sections[Math.Min(source.SectionIndex, source.State.Song.Sections.Length - 1)].LengthTicks - source.TicksLeft
		: -1;

	/// <summary>
	/// The section the song would play after the one playing, with the cues as they are now and on the pass playing: where
	/// the first of its exits that holds goes, else itself. Null when nothing plays. For a tool to show.
	/// </summary>
	public Section? NextSection => NextExit is var exit && SectionPlaying is { } current
		? exit >= 0 ? current.Exits[exit].To : current
		: null;

	/// <summary>
	/// Which of the playing section's <see cref="Section.Exits"/> would be taken were it ending now, with the cues as they
	/// are and on the pass playing (each condition as <see cref="Condition.IsMet(Func{CueBase, float}, int)"/> reads it,
	/// without the sequencer's latching), or -1 for none (it plays again, or nothing plays).
	/// </summary>
	public int NextExit
	{
		get
		{
			if (SectionPlaying is not { } current || _source is not { } source)
				return -1;
			for (var e = 0; e < current.Exits.Count; e++)
				if (current.Exits[e].When is not { } when || when.IsMet(CueValue, source.Pass))
					return e;
			return -1;
		}
	}

	/// <summary>Which pass of the playing section in a row this is, 1 the first time; 0 with nothing playing.</summary>
	public int Pass => _source?.Pass ?? 0;

	// The section playing, as its song has it
	private Section? SectionPlaying => _source is { } source && Current is { } state
		? state.Song.Sections[Math.Min(source.SectionIndex, state.Song.Sections.Length - 1)].Source
		: null;

	/// <summary>
	/// What <paramref name="placement"/> plays over one pass of its part, as the sequencer has it: its notes or hits with
	/// their ticks, lengths and velocities, in the scale playing (or <paramref name="scale"/>, or its song's, when its song
	/// isn't the one playing). For a tool drawing the part; it allocates, so not for every frame.
	/// </summary>
	public PlacementPattern Pattern(Placement placement, Scale? scale = null)
	{
		ArgumentNullException.ThrowIfNull(placement);
		var song = placement.Section.Song;
		var compiled = Current is { } playing && playing.Song.Song == song ? playing.Song : CompiledSong.Compile(song, scale ?? song.Scale);
		var section = compiled.Sections[placement.Section.Index];
		var part = Array.Find(section.Placements, p => p.Source == placement)
			?? throw new ArgumentException($"Placement '{placement.Name}' isn't in its song as compiled.", nameof(placement));
		var events = new List<PatternEvent>();
		foreach (var e in part.Events)
			if (e.Tick < part.LoopTicks)
				events.Add(new PatternEvent(e.Tick, e.Length, e.Note, e.Piece, e.Velocity));
		return new PlacementPattern(part.LoopTicks, compiled.TicksPerBar, compiled.Tracks[part.Track].Kit, events);
	}

	/// <summary>
	/// What <paramref name="placement"/> plays through its whole section, every pass and its fill, as the sequencer has it,
	/// in the scale playing (or <paramref name="scale"/>, or its song's, when its song isn't the one playing). For a tool
	/// drawing a lane; it allocates, so not for every frame.
	/// </summary>
	public SectionPattern SectionPatternOf(Placement placement, Scale? scale = null)
	{
		ArgumentNullException.ThrowIfNull(placement);
		var (compiled, section, part) = Compiled(placement, scale);
		var events = new List<PatternEvent>(part.Events.Length);
		foreach (var e in part.Events)
			events.Add(new PatternEvent(e.Tick, e.Length, e.Note, e.Piece, e.Velocity));
		var fill = placement.Fill is null ? -1 : section.LengthTicks - compiled.TicksPerBar;
		return new SectionPattern(section.LengthTicks, part.LoopTicks, fill, compiled.TicksPerBar, compiled.Tracks[part.Track].Kit, events);
	}

	/// <summary>
	/// Where <paramref name="placement"/>'s part is now: which pass of it, of how many, and the tick within that pass, or
	/// not playing while its section isn't. A part that doesn't play now (its condition unmet) still has a place in its
	/// section. Allocates nothing once the song is playing.
	/// </summary>
	public PlacementPosition PositionOf(Placement placement)
	{
		ArgumentNullException.ThrowIfNull(placement);
		if (_source is not { } source || Current is not { } state || state.Song.Song != placement.Section.Song
			|| source.SectionIndex != placement.Section.Index)
			return default;
		var compiled = state.Song;
		var section = compiled.Sections[placement.Section.Index];
		CompiledPlacement? part = null;
		foreach (var p in section.Placements)
			if (p.Source == placement)
				part = p;
		if (part is null || part.LoopTicks <= 0)
			return default;
		var tick = Math.Clamp(SectionTick, 0, section.LengthTicks - 1);
		var limit = placement.Fill is null ? section.LengthTicks : section.LengthTicks - compiled.TicksPerBar;
		var passes = Math.Max(1, (limit + part.LoopTicks - 1) / part.LoopTicks);
		if (tick >= limit)
			return new PlacementPosition(true, passes - 1, passes, tick - limit, part.LoopTicks, InFill: placement.Fill is not null);
		return new PlacementPosition(true, tick / part.LoopTicks, passes, tick % part.LoopTicks, part.LoopTicks, false);
	}

	/// <summary>
	/// Which of <paramref name="track"/>'s notes sounded as the last block ended: bit n of the 128 for MIDI note n, or for a
	/// kit's track its piece n (a hit lit for a moment). For a tool lighting the keys a track plays; it allocates nothing.
	/// Rendered ahead, up to the lead ahead of what is heard.
	/// </summary>
	public UInt128 Sounding(SongTrack track)
	{
		var index = CheckTrack(track).Index;
		if (_source is not { } source || (_swapping is not null && !ReferenceEquals(source.State, _swapping)))
			return UInt128.Zero;
		return source.Sounding(index);
	}

	// The song as compiled for a placement's song, its section and the placement in it
	private (CompiledSong Song, CompiledSection Section, CompiledPlacement Part) Compiled(Placement placement, Scale? scale)
	{
		var song = placement.Section.Song;
		var compiled = Current is { } playing && playing.Song.Song == song ? playing.Song : CompiledSong.Compile(song, scale ?? song.Scale);
		var section = compiled.Sections[placement.Section.Index];
		var part = Array.Find(section.Placements, p => p.Source == placement)
			?? throw new ArgumentException($"Placement '{placement.Name}' isn't in its song as compiled.", nameof(placement));
		return (compiled, section, part);
	}

	/// <summary>How busy its chip was at its last block.</summary>
	public ChipStats Stats => _source?.Stats ?? default;

	// The state the game's calls go to: a swap posted, else the one playing
	private SongState? Current => _swapping ?? _source?.State;

	/// <summary>
	/// Makes <paramref name="song"/> ready to play, in <paramref name="scale"/> or its own: compiles it and makes its chip's
	/// voices and effects, the costly part of starting a song. It touches nothing of the player's or the engine's, so it may
	/// run on any thread, as a game does it while a level loads; hand the result to <see cref="Play(PreparedSong, float)"/>.
	/// </summary>
	public PreparedSong Prepare(Song song, Scale? scale = null)
	{
		ArgumentNullException.ThrowIfNull(song);
		var state = new SongState(CompiledSong.Compile(song, scale ?? song.Scale));
		state.PrepareCore(_audio.SampleRate);
		return new PreparedSong(state);
	}

	/// <summary>
	/// Plays a song made ready by <see cref="Prepare"/>, from its first section. Anything playing fades out over
	/// <paramref name="fadeSeconds"/> as it starts. Every cue the player has been told is in force from the first bar.
	/// </summary>
	/// <exception cref="InvalidOperationException">It has been played already: prepare it again.</exception>
	public void Play(PreparedSong prepared, float fadeSeconds = 1f)
	{
		ArgumentNullException.ThrowIfNull(prepared);
		if (prepared.Played)
			throw new InvalidOperationException($"Song '{prepared.Song.Name}' was prepared once and has been played: prepare it again to play it again.");
		prepared.Played = true;
		var state = prepared.State;
		Tell(state);
		Stop(fadeSeconds);
		_muted.Clear();
		_soloed.Clear();
		_gainDb.Clear();
		_forced.Clear();
		_source = new MusicSource(state);
		_played = _aheadSeconds > 0 ? new AheadSource(_audio, _source, _aheadSeconds) : _source;
		_swapping = null;
		_audio.Play(_played, _bus);
	}

	/// <summary>
	/// Plays <paramref name="song"/>, in <paramref name="scale"/> or its own, from its first section. Anything playing fades
	/// out over <paramref name="fadeSeconds"/> as it starts. Every cue the player has been told is in force from the first bar.
	/// </summary>
	public void Play(Song song, Scale? scale = null, float fadeSeconds = 1f) => Play(Prepare(song, scale), fadeSeconds);

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
		Carry(state);
		// Its chip made ready here, before the audio thread first sees it through the queue
		state.PrepareCore(_audio.SampleRate);
		_swapping = state;
		source.Post(new MusicCommand { Kind = MusicCommandKind.Swap, Payload = state });
	}

	/// <summary>Stops the song, fading out over <paramref name="fadeSeconds"/>.</summary>
	public void Stop(float fadeSeconds = 1f)
	{
		if (_source is null || _played is not { } played)
			return;
		_audio.Stop(played, fadeSeconds);
		_fading.Add(played);
		_source = null;
		_played = null;
		_swapping = null;
	}

	/// <summary>Once a frame: lets go of a song that has faded out, and of a swap once it is taken.</summary>
	public void Update()
	{
		for (var i = _fading.Count - 1; i >= 0; i--)
			if (!_fading[i].IsPlaying)
			{
				(_fading[i] as AheadSource)?.Dispose();
				_fading.RemoveAt(i);
			}
		if (_source is { } source && _swapping is not null && ReferenceEquals(source.State, _swapping))
			_swapping = null;
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

	/// <summary>Any cue's value as last told, or its initial value: a choice's as its number (<see cref="CueBase.OptionValue"/>).</summary>
	public float Get(CueBase cue)
	{
		ArgumentNullException.ThrowIfNull(cue);
		return CueValue(cue);
	}

	/// <summary>
	/// Tells the music any cue's value as the player holds it: a level, or a choice's number (<see cref="CueBase.OptionValue"/>).
	/// For a tool that shows every kind of cue alike; a game uses the typed overloads.
	/// </summary>
	public void Set(CueBase cue, float value)
	{
		ArgumentNullException.ThrowIfNull(cue);
		Write(cue, value);
	}

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

	/// <summary>Silences a track's new notes, or lets them sound again. Kept, by the track's name, through a swap.</summary>
	public void Mute(SongTrack track, bool muted)
	{
		var index = CheckTrack(track).Index;
		_muted[track.Name] = muted;
		Post(MusicCommandKind.Mute, index, value: muted ? 1f : 0f);
	}

	/// <summary>Silences every track not soloed while any is. Kept, by the track's name, through a swap.</summary>
	public void Solo(SongTrack track, bool solo)
	{
		var index = CheckTrack(track).Index;
		_soloed[track.Name] = solo;
		Post(MusicCommandKind.Solo, index, value: solo ? 1f : 0f);
	}

	/// <summary>A track's gain over its level, in decibels: 0 as written. Kept, by the track's name, through a swap.</summary>
	public void SetTrackGain(SongTrack track, float db)
	{
		var index = CheckTrack(track).Index;
		_gainDb[track.Name] = db;
		Post(MusicCommandKind.TrackGain, index, value: Dsp.Units.DbToGain(db));
	}

	/// <summary>
	/// Holds a placement on or off whatever its condition, from the next bar line (or its next phrase), or back to
	/// <see cref="PlacementState.Auto"/>. Kept, by its section's and its own name, through a swap.
	/// </summary>
	/// <remarks>
	/// Every steering call is a command the audio thread takes in order, applied to the song swapped in if one is waiting,
	/// so a call made between a <see cref="Swap"/> and the bar line it lands at is kept.
	/// </remarks>
	public void Force(Placement placement, PlacementState state)
	{
		var (section, index) = Find(placement);
		_forced[(placement.Section.Name, placement.Name)] = state;
		Post(MusicCommandKind.Force, section, index, (int)state);
	}

	// What the game steered, by name, into a song about to be swapped in: mutes, solos, gains and forced placements
	private void Carry(SongState state)
	{
		var song = state.Song;
		for (var t = 0; t < song.Tracks.Length; t++)
		{
			var name = song.Tracks[t].Name;
			state.Muted[t] = _muted.GetValueOrDefault(name);
			state.Soloed[t] = _soloed.GetValueOrDefault(name);
			var gain = Dsp.Units.DbToGain(_gainDb.GetValueOrDefault(name));
			state.TrackGain[t] = gain;
			state.Instruments[t].Gain = song.Tracks[t].Gain * gain;
		}
		for (var s = 0; s < song.Sections.Length; s++)
			for (var p = 0; p < song.Sections[s].Placements.Length; p++)
				state.Forced[s][p] = _forced.GetValueOrDefault((song.Sections[s].Name, song.Sections[s].Placements[p].Name));
	}

	/// <summary>
	/// Whether a placement is playing now: its section is, and it is on. Rendered ahead, up to the lead ahead of what is heard.
	/// </summary>
	public bool IsActive(Placement placement)
	{
		var (section, index) = Find(placement);
		var source = _source!;
		// A song swapped in but not yet playing: nothing of it is on
		if (_swapping is not null && !ReferenceEquals(source.State, _swapping))
			return false;
		return source.SectionIndex == section && index < source.State.Published.Length && source.State.Published[index];
	}

	/// <summary>
	/// The loudest a track's notes were in the last block rendered, a linear level: for a meter. Rendered ahead, up to the
	/// lead ahead of what is heard.
	/// </summary>
	public float TrackPeak(SongTrack track)
	{
		var index = CheckTrack(track).Index;
		var source = _source!;
		if (_swapping is not null && !ReferenceEquals(source.State, _swapping))
			return 0f;
		var peaks = source.State.Peaks;
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
		{
			_audio.Stop(source);
			(source as AheadSource)?.Dispose();
		}
		_fading.Clear();
	}
}
