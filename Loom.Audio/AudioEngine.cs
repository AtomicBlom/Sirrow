using System.Numerics;
using System.Runtime.CompilerServices;

using Loom.Audio.Decoding;
using Loom.Audio.Decoding.Flac;
using Loom.Audio.Decoding.Vorbis;
using Loom.Audio.Mixing;

namespace Loom.Audio;

/// <summary>
/// An app's audio: clips and sources playing on voices, mixed through a tree of buses to a device. A
/// <c>LoomApplication</c> opens one for itself when <c>AppOptions.Audio</c> is set (its <c>Audio</c> property); a tool or
/// test can make one over any <see cref="IAudioDevice"/>.
/// <code>
/// var music = Audio.CreateBus("music");
/// var sfx = Audio.CreateBus("sfx");
/// var hit = Audio.LoadWav(ReadContent("sounds/hit.wav"));
/// Audio.Play(hit, sfx, volume: 0.8f, pitch: 1.1f);
/// var synth = new MySynth();
/// Audio.Play(synth, music);
/// synth.Cutoff.Value = 1200f; // an AudioParameter, read on the audio thread
/// </code>
/// </summary>
/// <remarks>
/// Two threads share it. The thread that made the engine (the app's frame thread) calls its methods; each call queues a
/// command the audio thread takes at the start of its next block, a few milliseconds later, so a play is heard after at
/// most one device buffer. The audio thread mixes and never waits on the game: there is no lock between them, and
/// neither side allocates once the engine is running. A call from another thread throws. Making clips
/// (<see cref="CreateClip"/>, <see cref="LoadWav"/>) is the exception: it touches no shared state, so a loading thread
/// can make them.
/// </remarks>
public sealed class AudioEngine : IDisposable
{
	private readonly AudioEventQueue<AudioCommand> _commands;
	private readonly AudioEventQueue<int> _ended;
	private readonly AudioMixer _mixer;
	private readonly int _ownerThread;

	// The voice pool, as the game thread sees it: a slot is busy from its play until the audio thread says it ended
	private readonly int[] _generations;
	private readonly bool[] _playing;
	private readonly AudioSource?[] _sources;
	private readonly int[] _free;
	private int _freeCount;
	private int _activeVoices;
	private int _droppedVoices;

	private readonly string[] _busNames;
	private readonly float[] _busVolumes;
	private int _busCount = 1;
	private readonly int[] _busParents;
	private readonly bool[] _duckEnabled;
	private int _duckCount;
	private float _unfocusedVolume;
	private bool _focused = true;
	// Open streams, replaced whole as they open and close, and what keeps their rings topped up: a thread of their own, or
	// in the browser (where the mix itself runs on the frame thread) Update
	private AudioStream[] _streams = [];
	private AudioStreamer? _streamer;
	private readonly bool _pumpStreams = OperatingSystem.IsBrowser();
	// Which voices were played in 3D, by slot, and the listener as last sent (so an unmoved listener sends nothing)
	private readonly bool[] _spatial;
	private (Vector3? Position, Vector3? Forward, Vector3? Up, Vector3? Velocity) _listenerSent;
	private bool _disposed;

	/// <summary>An engine playing through <paramref name="device"/>, which it starts and from then on owns (disposing it with itself).</summary>
	public AudioEngine(IAudioDevice device, AudioOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(device);
		options ??= new AudioOptions();
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxVoices, nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBuses, nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.CommandCapacity, nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegative(options.MaxDucks, nameof(options));
		if (!float.IsFinite(options.SpeedOfSound) || options.SpeedOfSound <= 0f)
			throw new ArgumentOutOfRangeException(nameof(options), options.SpeedOfSound, "AudioOptions.SpeedOfSound is more than zero, in the game's units a second.");
		if (!float.IsFinite(options.UnfocusedVolume) || options.UnfocusedVolume < 0f)
			throw new ArgumentOutOfRangeException(nameof(options), options.UnfocusedVolume, "AudioOptions.UnfocusedVolume is a linear gain, zero or more.");
		if (device.SampleRate <= 0)
			throw new ArgumentException($"The device reports a sample rate of {device.SampleRate}.", nameof(device));

		Device = device;
		SampleRate = device.SampleRate;
		_ownerThread = Environment.CurrentManagedThreadId;

		_commands = new AudioEventQueue<AudioCommand>(options.CommandCapacity);
		_ended = new AudioEventQueue<int>(options.MaxVoices);
		_generations = new int[options.MaxVoices];
		_playing = new bool[options.MaxVoices];
		_sources = new AudioSource?[options.MaxVoices];
		_free = new int[options.MaxVoices];
		for (var i = 0; i < _free.Length; i++)
			_free[i] = _free.Length - 1 - i; // the lowest slot is handed out first
		_freeCount = _free.Length;

		_busNames = new string[options.MaxBuses];
		_busVolumes = new float[options.MaxBuses];
		_busNames[0] = "master";
		_busVolumes[0] = 1f;

		_busParents = new int[options.MaxBuses];
		_duckEnabled = new bool[options.MaxDucks];
		_unfocusedVolume = options.UnfocusedVolume;

		_spatial = new bool[options.MaxVoices];
		_mixer = new AudioMixer(SampleRate, options.MaxVoices, options.MaxBuses, options.MaxDucks, options.SpeedOfSound, _commands, _ended);
		device.Start(_mixer);
	}

	/// <summary>The device the engine plays through: its backend, name, rate and buffer as granted.</summary>
	public IAudioDevice Device { get; }

	/// <summary>The engine's rate, in frames per second: the device's.</summary>
	public int SampleRate { get; }

	/// <summary>The bus everything ends in: what the device plays.</summary>
	public AudioBus Master => AudioBus.Master;

	/// <summary>How the mixer is doing: its clock, its load against the device's deadline, and its voices.</summary>
	public AudioStats Stats => new(
		_mixer.FramesRendered,
		_mixer.Callbacks,
		_mixer.LastCallbackFrames,
		_activeVoices,
		_droppedVoices,
		_mixer.Peak,
		_mixer.RenderLoad,
		_mixer.MaxRenderLoad);

	/// <summary>
	/// A new bus named <paramref name="name"/>, mixed into <paramref name="parent"/> (the master bus by default). Make the
	/// buses at load: there are at most <see cref="AudioOptions.MaxBuses"/>, and none is ever removed.
	/// </summary>
	public AudioBus CreateBus(string name, AudioBus parent = default)
	{
		CheckThread();
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		CheckBus(parent);
		if (_busCount == _busNames.Length)
			throw new InvalidOperationException($"The engine already holds its {_busNames.Length} buses: raise AudioOptions.MaxBuses for more.");
		var index = _busCount;
		Send(new AudioCommand { Kind = AudioCommandKind.AddBus, Index = index, Bus = parent.Index });
		_busCount++;
		_busNames[index] = name;
		_busParents[index] = parent.Index;
		_busVolumes[index] = 1f;
		return new AudioBus(index);
	}

	/// <summary>The name <paramref name="bus"/> was made with.</summary>
	public string GetBusName(AudioBus bus)
	{
		CheckBus(bus);
		return _busNames[bus.Index];
	}

	/// <summary>The volume last set on <paramref name="bus"/> (where it is going, if it is still fading).</summary>
	public float GetBusVolume(AudioBus bus)
	{
		CheckBus(bus);
		return _busVolumes[bus.Index];
	}

	/// <summary>
	/// Sets <paramref name="bus"/>'s volume, a linear gain (1 leaves it as it is, 0 silences it), fading over
	/// <paramref name="fadeSeconds"/>. Everything mixed into the bus follows it.
	/// </summary>
	public void SetBusVolume(AudioBus bus, float volume, float fadeSeconds = 0f)
	{
		CheckThread();
		CheckBus(bus);
		CheckVolume(volume);
		Send(new AudioCommand { Kind = AudioCommandKind.BusVolume, Index = bus.Index, A = volume, Frames = FramesFor(fadeSeconds) });
		_busVolumes[bus.Index] = volume;
	}

	/// <summary>
	/// Turns <paramref name="target"/> down while <paramref name="trigger"/> has sound on it: music under dialogue, ambience
	/// under a cutscene's sound. The duck follows the trigger's level, not just whether a voice is playing on it, so the
	/// target comes back up in a pause, after <paramref name="holdSeconds"/>. Make ducks at load, like buses: there are at most
	/// <see cref="AudioOptions.MaxDucks"/>, and none is removed (<see cref="SetDuckEnabled"/> turns one off). A bus several ducks
	/// turn down takes all their depths.
	/// </summary>
	/// <param name="trigger">The bus listened to: its level after its own volume.</param>
	/// <param name="target">The bus turned down. It can't be the trigger, nor a bus the trigger mixes into, which would turn the trigger down with it.</param>
	/// <param name="depthDb">How far down the target goes, in decibels: below zero (-6 halves it, -12 is a quarter).</param>
	/// <param name="attackSeconds">How long the target takes to go all the way down.</param>
	/// <param name="releaseSeconds">How long it takes to come all the way back.</param>
	/// <param name="thresholdDb">The trigger's level, in decibels of full scale, above which it counts as sounding.</param>
	/// <param name="holdSeconds">How long the target stays down after the trigger falls below the threshold.</param>
	public AudioDuck AddDuck(AudioBus trigger, AudioBus target, float depthDb, float attackSeconds = 0.05f, float releaseSeconds = 0.4f,
		float thresholdDb = -40f, float holdSeconds = 0.15f)
	{
		CheckThread();
		CheckBus(trigger);
		CheckBus(target);
		if (trigger == target)
			throw new ArgumentException("A bus can't duck itself.", nameof(target));
		if (Contains(target.Index, trigger.Index))
			throw new ArgumentException(
				$"'{_busNames[trigger.Index]}' is mixed into '{_busNames[target.Index]}', so ducking it would turn the trigger down too.", nameof(target));
		if (Contains(trigger.Index, target.Index))
			throw new ArgumentException(
				$"'{_busNames[target.Index]}' is mixed into '{_busNames[trigger.Index]}', so ducking it would lower the trigger's level and let the duck go: it would pump.", nameof(target));
		if (!float.IsFinite(depthDb) || depthDb >= 0f)
			throw new ArgumentOutOfRangeException(nameof(depthDb), depthDb, "A duck's depth is below zero decibels: how far the target goes down.");
		if (!float.IsFinite(thresholdDb))
			throw new ArgumentOutOfRangeException(nameof(thresholdDb), thresholdDb, "A threshold is a level in decibels of full scale, such as -40.");
		var attack = FramesFor(attackSeconds);
		var release = FramesFor(releaseSeconds);
		var hold = FramesFor(holdSeconds);
		if (_duckCount == _duckEnabled.Length)
			throw new InvalidOperationException($"The engine already holds its {_duckEnabled.Length} ducks: raise AudioOptions.MaxDucks for more.");

		var index = _duckCount;
		var settings = new DuckSettings(
			trigger.Index,
			target.Index,
			depthDb,
			MathF.Pow(10f, thresholdDb / 20f),
			attack > 0 ? -depthDb / attack : float.PositiveInfinity,
			release > 0 ? -depthDb / release : float.PositiveInfinity,
			hold);
		Send(new AudioCommand { Kind = AudioCommandKind.AddDuck, Index = index, Payload = settings });
		_duckCount++;
		_duckEnabled[index] = true;
		return new AudioDuck(index);
	}

	/// <summary>
	/// Turns <paramref name="duck"/> on or off. Off, its target comes back up at the duck's release rate however loud the
	/// trigger is; on again, it ducks from the next block the trigger is heard.
	/// </summary>
	public void SetDuckEnabled(AudioDuck duck, bool enabled)
	{
		CheckThread();
		CheckDuck(duck);
		Send(new AudioCommand { Kind = AudioCommandKind.DuckEnabled, Index = duck.Index, A = enabled ? 1f : 0f });
		_duckEnabled[duck.Index] = enabled;
	}

	/// <summary>Whether <paramref name="duck"/> is on, as last set.</summary>
	public bool IsDuckEnabled(AudioDuck duck)
	{
		CheckDuck(duck);
		return _duckEnabled[duck.Index];
	}

	/// <summary>
	/// Tells the engine whether the app has focus: out of it, the whole output fades to <see cref="UnfocusedVolume"/>
	/// over a quarter of a second, and back on return. A <c>LoomApplication</c> calls it as its window or tab gains and loses
	/// focus; the game's own volumes are untouched.
	/// </summary>
	public void SetFocused(bool focused)
	{
		CheckThread();
		_focused = focused;
		Send(new AudioCommand { Kind = AudioCommandKind.OutputVolume, A = focused ? 1f : _unfocusedVolume, Frames = FramesFor(0.25f) });
	}

	/// <summary>
	/// The output's volume while the app is out of focus, a linear gain: <see cref="AudioOptions.UnfocusedVolume"/> until
	/// <see cref="SetUnfocusedVolume"/> changes it. It multiplies with every bus's volume, the master's included.
	/// </summary>
	public float UnfocusedVolume => _unfocusedVolume;

	/// <summary>
	/// Changes the output's volume while the app is out of focus, for a setting the player changes as the game runs. In
	/// focus nothing is heard to change; out of focus the output fades to it over <paramref name="fadeSeconds"/>.
	/// </summary>
	/// <param name="volume">A linear gain, zero or more: 1 leaves the app as loud out of focus as in it, 0 silences it.</param>
	/// <param name="fadeSeconds">How long the output takes to reach it when the app is out of focus now.</param>
	public void SetUnfocusedVolume(float volume, float fadeSeconds = 0.05f)
	{
		CheckThread();
		CheckVolume(volume);
		var frames = FramesFor(fadeSeconds);
		_unfocusedVolume = volume;
		if (!_focused)
			Send(new AudioCommand { Kind = AudioCommandKind.OutputVolume, A = volume, Frames = frames });
	}

	/// <summary>
	/// Plays <paramref name="clip"/> on <paramref name="bus"/>. Returns the voice, to change or stop it, or
	/// <see cref="VoiceHandle.None"/> when every voice is busy.
	/// </summary>
	/// <param name="clip">A clip made by this engine.</param>
	/// <param name="bus">Where the voice is mixed: the master bus by default.</param>
	/// <param name="volume">A linear gain: 1 plays the clip as it is.</param>
	/// <param name="pitch">How fast the clip plays: 2 is an octave up and twice as fast, 0.5 an octave down.</param>
	/// <param name="pan">-1 full left, 0 centre, 1 full right.</param>
	/// <param name="loop">Whether the clip starts over at its end, playing until stopped.</param>
	public VoiceHandle Play(AudioClip clip, AudioBus bus = default, float volume = 1f, float pitch = 1f, float pan = 0f, bool loop = false)
	{
		CheckThread();
		ArgumentNullException.ThrowIfNull(clip);
		if (clip.SampleRate != SampleRate)
			throw new ArgumentException($"The clip is at {clip.SampleRate} Hz and this engine at {SampleRate} Hz: make clips with this engine's CreateClip or LoadWav.", nameof(clip));
		CheckBus(bus);
		CheckVolume(volume);
		CheckPitch(pitch);
		CheckPan(pan);
		if (!TryTakeVoice(out var slot, out var generation))
			return VoiceHandle.None;
		StartVoice(slot, new AudioCommand { Kind = AudioCommandKind.PlayClip, Index = slot, Bus = bus.Index, A = volume, B = pitch, C = pan, Loop = loop, Payload = clip });
		return new VoiceHandle(slot, generation);
	}

	/// <summary>
	/// Plays <paramref name="source"/> on <paramref name="bus"/>, until it finishes (its <see cref="AudioSource.Render"/>
	/// returns false) or is stopped. Returns the voice, or <see cref="VoiceHandle.None"/> when every voice is busy.
	/// </summary>
	/// <param name="source">
	/// A source not already playing. One that has finished, or been stopped and faded out, can play again: a stopped source
	/// keeps playing until the audio thread has rendered its fade, so <see cref="Stop(AudioSource, float)"/> followed at once
	/// by a play of the same source throws.
	/// </param>
	/// <param name="bus">Where the voice is mixed: the master bus by default.</param>
	/// <param name="volume">A linear gain: 1 plays the source as it renders.</param>
	/// <param name="pan">-1 full left, 0 centre, 1 full right: a balance, since a source is stereo.</param>
	/// <exception cref="InvalidOperationException">The source is still playing on a voice, or fading out after a stop.</exception>
	public VoiceHandle Play(AudioSource source, AudioBus bus = default, float volume = 1f, float pan = 0f)
	{
		CheckThread();
		ArgumentNullException.ThrowIfNull(source);
		CheckBus(bus);
		CheckVolume(volume);
		CheckPan(pan);
		// Hear the voices that ended since the last Update, so a source that finished or faded out since then is free
		TakeEnded();
		if (source.IsPlaying)
			throw new InvalidOperationException(
				"The source is still playing: a source plays on one voice at a time, and a stopped one plays on until the audio " +
				"thread has rendered its fade. Play it again once AudioSource.IsPlaying is false, or make another instance.");
		source.Start(SampleRate);
		if (!TryTakeVoice(out var slot, out var generation))
			return VoiceHandle.None;
		_sources[slot] = source;
		source.VoiceSlot = slot;
		StartVoice(slot, new AudioCommand { Kind = AudioCommandKind.PlaySource, Index = slot, Bus = bus.Index, A = volume, C = pan, Payload = source });
		return new VoiceHandle(slot, generation);
	}

	/// <summary>
	/// Places the listener: where the game hears from, usually its camera. 3D voices are heard from here, panned by which
	/// side of the listener they are on and quieter with distance; the velocity shifts their pitch (Doppler). Call it each
	/// frame the listener moves. Right-handed, as <see cref="Matrix4x4.CreateLookAt"/> is: the listener's right is
	/// <paramref name="forward"/> × <paramref name="up"/>. Until it is set, the listener is at the origin facing -Z, with +Y up.
	/// </summary>
	/// <param name="position">Where the listener is, in the game's units.</param>
	/// <param name="forward">Which way it faces. Needn't be unit length.</param>
	/// <param name="up">Which way is up for it. Needn't be unit length, nor exactly square to <paramref name="forward"/>, but not along it.</param>
	/// <param name="velocity">How fast it moves, in units a second: for Doppler. Zero for none.</param>
	public void SetListener(Vector3 position, Vector3 forward, Vector3 up, Vector3 velocity = default)
	{
		CheckThread();
		CheckVector(position, nameof(position));
		CheckVector(velocity, nameof(velocity));
		if (!IsFinite(forward) || !IsFinite(up) || forward.LengthSquared() < 1e-12f || up.LengthSquared() < 1e-12f
			|| Vector3.Cross(forward, up).LengthSquared() < 1e-12f * forward.LengthSquared() * up.LengthSquared())
			throw new ArgumentException("The listener's forward and up are finite, not zero, and not along each other.", nameof(forward));
		forward = Vector3.Normalize(forward);
		up = Vector3.Normalize(up);

		SendVector(AudioCommandKind.ListenerPosition, -1, position, ref _listenerSent.Position);
		SendVector(AudioCommandKind.ListenerForward, -1, forward, ref _listenerSent.Forward);
		SendVector(AudioCommandKind.ListenerUp, -1, up, ref _listenerSent.Up);
		SendVector(AudioCommandKind.ListenerVelocity, -1, velocity, ref _listenerSent.Velocity);
	}

	/// <summary>
	/// Plays <paramref name="clip"/> as a 3D voice at <paramref name="position"/>: panned and attenuated for the listener
	/// (<see cref="SetListener"/>), re-placed every block. A stereo clip is heard as one point, its channels mixed. Returns the
	/// voice, to move with <see cref="SetPosition"/>, or <see cref="VoiceHandle.None"/> when every voice is busy.
	/// </summary>
	/// <param name="clip">A clip made by this engine.</param>
	/// <param name="position">Where it sounds from, in the game's units.</param>
	/// <param name="bus">Where the voice is mixed: the master bus by default.</param>
	/// <param name="volume">A linear gain at the minimum distance or nearer.</param>
	/// <param name="pitch">How fast the clip plays, before any Doppler shift.</param>
	/// <param name="loop">Whether the clip starts over at its end, playing until stopped.</param>
	/// <param name="spatial">How it falls off and shifts: <see cref="AudioSpatial"/>'s defaults when null.</param>
	public VoiceHandle PlayAt(AudioClip clip, Vector3 position, AudioBus bus = default, float volume = 1f, float pitch = 1f, bool loop = false, AudioSpatial? spatial = null)
	{
		CheckThread();
		ArgumentNullException.ThrowIfNull(clip);
		if (clip.SampleRate != SampleRate)
			throw new ArgumentException($"The clip is at {clip.SampleRate} Hz and this engine at {SampleRate} Hz: make clips with this engine's CreateClip or LoadWav.", nameof(clip));
		CheckBus(bus);
		CheckVolume(volume);
		CheckPitch(pitch);
		CheckVector(position, nameof(position));
		var settings = CheckSpatial(spatial ?? new AudioSpatial());
		if (!TryTakeVoice(out var slot, out var generation))
			return VoiceHandle.None;
		StartSpatialVoice(slot, position, settings, new AudioCommand { Kind = AudioCommandKind.PlayClip, Index = slot, Bus = bus.Index, A = volume, B = pitch, Loop = loop, Payload = clip });
		return new VoiceHandle(slot, generation);
	}

	/// <summary>
	/// Plays <paramref name="source"/> (a stream included) as a 3D voice at <paramref name="position"/>, as
	/// <see cref="PlayAt(AudioClip, Vector3, AudioBus, float, float, bool, AudioSpatial?)"/> plays a clip: its two channels
	/// heard as one point. A source has no pitch, so it isn't Doppler-shifted. The rules for playing a source are
	/// <see cref="Play(AudioSource, AudioBus, float, float)"/>'s.
	/// </summary>
	public VoiceHandle PlayAt(AudioSource source, Vector3 position, AudioBus bus = default, float volume = 1f, AudioSpatial? spatial = null)
	{
		CheckThread();
		ArgumentNullException.ThrowIfNull(source);
		CheckBus(bus);
		CheckVolume(volume);
		CheckVector(position, nameof(position));
		var settings = CheckSpatial(spatial ?? new AudioSpatial());
		TakeEnded();
		if (source.IsPlaying)
			throw new InvalidOperationException(
				"The source is still playing: a source plays on one voice at a time, and a stopped one plays on until the audio " +
				"thread has rendered its fade. Play it again once AudioSource.IsPlaying is false, or make another instance.");
		source.Start(SampleRate);
		if (!TryTakeVoice(out var slot, out var generation))
			return VoiceHandle.None;
		_sources[slot] = source;
		source.VoiceSlot = slot;
		StartSpatialVoice(slot, position, settings, new AudioCommand { Kind = AudioCommandKind.PlaySource, Index = slot, Bus = bus.Index, A = volume, Payload = source });
		return new VoiceHandle(slot, generation);
	}

	/// <summary>
	/// Moves a 3D voice to <paramref name="position"/>, heard from the next block. <paramref name="velocity"/>, in units a
	/// second, shifts its pitch (Doppler); zero for none. A stale handle is ignored.
	/// </summary>
	/// <exception cref="InvalidOperationException">The voice was played with <c>Play</c>, not <c>PlayAt</c>: it has no position.</exception>
	public void SetPosition(VoiceHandle voice, Vector3 position, Vector3 velocity = default)
	{
		CheckThread();
		CheckVector(position, nameof(position));
		CheckVector(velocity, nameof(velocity));
		if (!IsLive(voice))
			return;
		if (!_spatial[voice.Slot])
			throw new InvalidOperationException("The voice was played with Play, so it has no position: play it with PlayAt to place it in 3D.");
		Send(new AudioCommand { Kind = AudioCommandKind.VoicePosition, Index = voice.Slot, A = position.X, B = position.Y, C = position.Z });
		Send(new AudioCommand { Kind = AudioCommandKind.VoiceVelocity, Index = voice.Slot, A = velocity.X, B = velocity.Y, C = velocity.Z });
	}

	// A 3D play: its settings, position and (still) velocity go ahead of it, and the play keeps them
	private void StartSpatialVoice(int slot, Vector3 position, in AudioSpatial settings, AudioCommand play)
	{
		var setup = new AudioCommand
		{
			Kind = AudioCommandKind.VoiceSpatial,
			Index = slot,
			A = settings.MinDistance,
			B = settings.MaxDistance,
			C = settings.RolloffFactor,
			D = settings.DopplerFactor,
			Frames = (int)settings.Rolloff,
		};
		if (!_commands.TryPost(setup)
			|| !_commands.TryPost(new AudioCommand { Kind = AudioCommandKind.VoicePosition, Index = slot, A = position.X, B = position.Y, C = position.Z })
			|| !_commands.TryPost(new AudioCommand { Kind = AudioCommandKind.VoiceVelocity, Index = slot }))
		{
			FreeVoice(slot);
			throw QueueFull();
		}
		play.Spatial = true;
		_spatial[slot] = true;
		StartVoice(slot, play);
	}

	private void SendVector(AudioCommandKind kind, int index, Vector3 value, ref Vector3? last)
	{
		if (last == value)
			return;
		Send(new AudioCommand { Kind = kind, Index = index, A = value.X, B = value.Y, C = value.Z });
		last = value;
	}

	private static AudioSpatial CheckSpatial(AudioSpatial spatial)
	{
		if (!float.IsFinite(spatial.MinDistance) || spatial.MinDistance <= 0f)
			throw new ArgumentOutOfRangeException(nameof(spatial), spatial.MinDistance, "AudioSpatial.MinDistance is more than zero.");
		if (!float.IsFinite(spatial.MaxDistance) || spatial.MaxDistance < spatial.MinDistance)
			throw new ArgumentOutOfRangeException(nameof(spatial), spatial.MaxDistance, "AudioSpatial.MaxDistance is at least MinDistance.");
		if (!float.IsFinite(spatial.RolloffFactor) || spatial.RolloffFactor < 0f)
			throw new ArgumentOutOfRangeException(nameof(spatial), spatial.RolloffFactor, "AudioSpatial.RolloffFactor is zero or more.");
		if (!float.IsFinite(spatial.DopplerFactor) || spatial.DopplerFactor < 0f)
			throw new ArgumentOutOfRangeException(nameof(spatial), spatial.DopplerFactor, "AudioSpatial.DopplerFactor is zero or more.");
		if (spatial.Rolloff is < AudioRolloff.Inverse or > AudioRolloff.Exponential)
			throw new ArgumentOutOfRangeException(nameof(spatial), spatial.Rolloff, "No such rolloff.");
		return spatial;
	}

	private static void CheckVector(Vector3 value, string name)
	{
		if (!IsFinite(value))
			throw new ArgumentOutOfRangeException(name, value, "A position or velocity is finite.");
	}

	private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

	/// <summary>Whether <paramref name="voice"/> is still playing, as the game thread last heard (at most a frame old).</summary>
	public bool IsPlaying(VoiceHandle voice)
	{
		CheckThread();
		return IsLive(voice);
	}

	/// <summary>Moves <paramref name="voice"/>'s volume to <paramref name="volume"/> over <paramref name="fadeSeconds"/>. A stale handle is ignored.</summary>
	public void SetVolume(VoiceHandle voice, float volume, float fadeSeconds = 0f)
	{
		CheckThread();
		CheckVolume(volume);
		if (IsLive(voice))
			Send(new AudioCommand { Kind = AudioCommandKind.VoiceVolume, Index = voice.Slot, A = volume, Frames = FramesFor(fadeSeconds) });
	}

	/// <summary>Sets a clip voice's pitch (see <see cref="Play(AudioClip, AudioBus, float, float, float, bool)"/>). A source voice ignores it, and a stale handle is ignored.</summary>
	public void SetPitch(VoiceHandle voice, float pitch)
	{
		CheckThread();
		CheckPitch(pitch);
		if (IsLive(voice))
			Send(new AudioCommand { Kind = AudioCommandKind.VoicePitch, Index = voice.Slot, A = pitch });
	}

	/// <summary>Moves <paramref name="voice"/>'s pan to <paramref name="pan"/> (-1 left, 1 right) across the next block. A stale handle is ignored.</summary>
	/// <exception cref="InvalidOperationException">The voice is a 3D one (<c>PlayAt</c>): its position pans it.</exception>
	public void SetPan(VoiceHandle voice, float pan)
	{
		CheckThread();
		CheckPan(pan);
		if (!IsLive(voice))
			return;
		if (_spatial[voice.Slot])
			throw new InvalidOperationException("The voice is a 3D one, panned by its position: move it with SetPosition instead.");
		Send(new AudioCommand { Kind = AudioCommandKind.VoicePan, Index = voice.Slot, A = pan });
	}

	/// <summary>
	/// Stops <paramref name="voice"/>, fading it out over <paramref name="fadeSeconds"/>; with no fade it still ramps to
	/// silence across one block, a few milliseconds, so it doesn't click. A stale handle is ignored.
	/// </summary>
	public void Stop(VoiceHandle voice, float fadeSeconds = 0f)
	{
		CheckThread();
		if (IsLive(voice))
			Send(new AudioCommand { Kind = AudioCommandKind.StopVoice, Index = voice.Slot, Frames = FramesFor(fadeSeconds) });
	}

	/// <summary>
	/// Stops <paramref name="source"/>'s voice, if it is playing (see <see cref="Stop(VoiceHandle, float)"/>). The source stays
	/// <see cref="AudioSource.IsPlaying"/> until the audio thread has rendered the fade and the engine has heard the voice end,
	/// at its next <see cref="Update"/> or play of a source; only then can it be played again.
	/// </summary>
	public void Stop(AudioSource source, float fadeSeconds = 0f)
	{
		ArgumentNullException.ThrowIfNull(source);
		CheckThread();
		if (source.VoiceSlot is var slot and >= 0)
			Stop(new VoiceHandle(slot, _generations[slot]), fadeSeconds);
	}

	/// <summary>
	/// A clip of <paramref name="samples"/> (interleaved when stereo) at <paramref name="sampleRate"/>, converted to the
	/// engine's rate. Any thread may call it.
	/// </summary>
	/// <param name="samples">The samples, -1..1.</param>
	/// <param name="channels">1 or 2.</param>
	/// <param name="sampleRate">The samples' rate, in frames per second.</param>
	public AudioClip CreateClip(ReadOnlySpan<float> samples, int channels, int sampleRate) =>
		ClipFactory.Create(samples, channels, sampleRate, SampleRate);

	/// <summary>
	/// A clip of a WAV file's contents (PCM at 8 to 32 bits or float, mono or stereo), converted to the engine's rate. Any
	/// thread may call it: decode on a loading thread and play on the game's.
	/// </summary>
	public AudioClip LoadWav(ReadOnlySpan<byte> wav)
	{
		var (samples, channels, rate) = WavDecoder.Decode(wav);
		return ClipFactory.Create(samples, channels, rate, SampleRate);
	}

	/// <summary>
	/// A clip of an Ogg Vorbis file's contents, decoded whole and converted to the engine's rate: for sound effects and
	/// short loops. Music is better streamed (<see cref="OpenStream(byte[], bool)"/>), which holds only the compressed file.
	/// Any thread may call it.
	/// </summary>
	/// <exception cref="InvalidDataException">The bytes aren't Ogg Vorbis, or are damaged.</exception>
	/// <exception cref="NotSupportedException">The file has more than two channels.</exception>
	public AudioClip LoadOgg(ReadOnlySpan<byte> ogg) => LoadDecoded(new VorbisDecoder(new MemoryStream(ogg.ToArray(), writable: false)));

	/// <summary>
	/// A clip of a FLAC file's contents, decoded whole and converted to the engine's rate. FLAC is lossless, about half the
	/// size of the same audio as WAV; streamed (<see cref="OpenStream(byte[], bool)"/>) it holds only the compressed file.
	/// Any thread may call it.
	/// </summary>
	/// <exception cref="InvalidDataException">The bytes aren't FLAC, or are damaged.</exception>
	/// <exception cref="NotSupportedException">The file has more than two channels, or changes its rate or channels partway.</exception>
	public AudioClip LoadFlac(ReadOnlySpan<byte> flac) => LoadDecoded(new FlacDecoder(new MemoryStream(flac.ToArray(), writable: false)));

	private AudioClip LoadDecoded(IStreamDecoder decoder)
	{
		CheckStreamChannels(decoder);
		var samples = new float[decoder.SampleRate * decoder.Channels];
		var count = 0;
		while (true)
		{
			if (count == samples.Length)
				Array.Resize(ref samples, samples.Length * 2);
			var frames = decoder.Read(samples.AsSpan(count));
			if (frames == 0)
				break;
			count += frames * decoder.Channels;
		}
		return ClipFactory.Create(samples.AsSpan(0, count), decoder.Channels, decoder.SampleRate, SampleRate);
	}

	/// <summary>
	/// Opens an Ogg Vorbis or FLAC file to stream (which, it tells from the file's first bytes): decoded as it plays, holding
	/// only the compressed bytes. Play it as any source, with <see cref="Play(AudioSource, AudioBus, float, float)"/>; each
	/// play starts from the beginning. Dispose it when done.
	/// </summary>
	/// <param name="file">The file's bytes, as <c>ReadContent</c> gives them.</param>
	/// <param name="loop">Whether it starts over at its end, without a gap, until stopped.</param>
	/// <exception cref="InvalidDataException">The bytes are neither Ogg Vorbis nor FLAC, or are damaged.</exception>
	/// <exception cref="NotSupportedException">The file has more than two channels.</exception>
	public AudioStream OpenStream(byte[] file, bool loop = false)
	{
		ArgumentNullException.ThrowIfNull(file);
		return OpenStream(new MemoryStream(file, writable: false), loop);
	}

	/// <summary>
	/// Opens an Ogg Vorbis or FLAC file to stream from <paramref name="file"/>, which the stream then owns and closes. A file
	/// stream is read as it plays, off the audio thread; a stream that can't seek (an Android asset's) is read into memory
	/// first, so it can loop and play again.
	/// </summary>
	/// <inheritdoc cref="OpenStream(byte[], bool)"/>
	public AudioStream OpenStream(Stream file, bool loop = false)
	{
		CheckThread();
		ArgumentNullException.ThrowIfNull(file);
		if (!file.CanSeek)
		{
			var copy = new MemoryStream();
			using (file)
				file.CopyTo(copy);
			copy.Position = 0;
			file = copy;
		}

		IStreamDecoder decoder;
		try
		{
			decoder = StreamDecoders.Open(file);
			CheckStreamChannels(decoder);
		}
		catch
		{
			file.Dispose();
			throw;
		}
		var stream = new AudioStream(this, file, decoder, loop);
		_streams = [.. _streams, stream];
		if (!_pumpStreams)
		{
			_streamer ??= new AudioStreamer();
			_streamer.SetStreams(_streams);
		}
		return stream;
	}

	// A stream disposing: its voice stopped, and no more decoding for it
	internal void Close(AudioStream stream)
	{
		CheckThread();
		Stop(stream);
		_streams = Array.FindAll(_streams, s => s != stream);
		_streamer?.SetStreams(_streams);
	}

	private static void CheckStreamChannels(IStreamDecoder decoder)
	{
		if (decoder.Channels > 2)
			throw new NotSupportedException($"The file has {decoder.Channels} channels: the engine plays mono and stereo.");
	}

	/// <summary>
	/// Hears from the audio thread: frees the voices that have ended, and throws what a source threw there or a stream's
	/// decoding threw. In the browser it also decodes ahead for the streams. A <c>LoomApplication</c> calls it once a frame;
	/// an engine used on its own should too.
	/// </summary>
	/// <exception cref="InvalidOperationException">An <see cref="AudioSource"/> threw on the audio thread, or an <see cref="AudioStream"/>'s file failed to decode (the inner exception); its voice was stopped.</exception>
	public void Update()
	{
		CheckThread();
		TakeEnded();
		foreach (var stream in _streams)
		{
			if (_pumpStreams)
				stream.Fill(int.MaxValue);
			if (stream.TakeFault() is { } failed)
				throw new InvalidOperationException($"An audio stream failed to decode, and stopped: {failed.Message}", failed);
		}
		if (Interlocked.Exchange(ref _mixer.Fault, null) is { } fault)
			throw new InvalidOperationException($"An audio source threw on the audio thread, and its voice was stopped: {fault.Message}", fault);
	}

	/// <summary>Silences the device until <see cref="Resume"/>: the app has left the screen. Voices hold where they are.</summary>
	public void Pause()
	{
		CheckThread();
		Device.Pause();
	}

	/// <summary>Starts the device again after <see cref="Pause"/>.</summary>
	public void Resume()
	{
		CheckThread();
		Device.Resume();
	}

	/// <summary>Stops and closes the device, and closes every open stream.</summary>
	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		Device.Dispose();
		_streamer?.Dispose();
		foreach (var stream in _streams)
			stream.Close();
		_streams = [];
	}

	private bool IsLive(VoiceHandle voice) =>
		!voice.IsNone && (uint)voice.Slot < (uint)_playing.Length && _playing[voice.Slot] && _generations[voice.Slot] == voice.Generation;

	private bool TryTakeVoice(out int slot, out int generation)
	{
		if (_freeCount == 0)
			TakeEnded();
		if (_freeCount == 0)
		{
			_droppedVoices++;
			slot = -1;
			generation = 0;
			return false;
		}
		slot = _free[--_freeCount];
		generation = _generations[slot] = _generations[slot] == int.MaxValue ? 1 : _generations[slot] + 1;
		_playing[slot] = true;
		_spatial[slot] = false;
		_activeVoices++;
		return true;
	}

	private void StartVoice(int slot, in AudioCommand command)
	{
		if (!_commands.TryPost(command))
		{
			FreeVoice(slot);
			throw QueueFull();
		}
	}

	private void TakeEnded()
	{
		while (_ended.TryTake(out var slot))
			FreeVoice(slot);
	}

	private void FreeVoice(int slot)
	{
		_playing[slot] = false;
		if (_sources[slot] is { } source)
		{
			source.VoiceSlot = -1;
			_sources[slot] = null;
		}
		_free[_freeCount++] = slot;
		_activeVoices--;
	}

	private void Send(in AudioCommand command)
	{
		if (!_commands.TryPost(command))
			throw QueueFull();
	}

	private InvalidOperationException QueueFull() => new(
		$"The audio command queue is full ({_commands.Capacity} waiting): the audio thread isn't taking them (is the device running?), " +
		"or the game sent more in one frame than AudioOptions.CommandCapacity allows.");

	private int FramesFor(float seconds)
	{
		if (!float.IsFinite(seconds) || seconds < 0f)
			throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A fade is zero or more seconds.");
		return (int)Math.Min(seconds * (double)SampleRate, int.MaxValue);
	}

	private void CheckThread([CallerMemberName] string member = "")
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (Environment.CurrentManagedThreadId != _ownerThread)
			throw new InvalidOperationException(
				$"AudioEngine.{member} was called from thread {Environment.CurrentManagedThreadId}, but the engine belongs to thread {_ownerThread}, " +
				"the one that made it: call it from there (the app's frame thread).");
	}

	private void CheckBus(AudioBus bus)
	{
		if ((uint)bus.Index >= (uint)_busCount)
			throw new ArgumentOutOfRangeException(nameof(bus), bus.Index, "No such bus in this engine.");
	}

	private void CheckDuck(AudioDuck duck)
	{
		if ((uint)duck.Index >= (uint)_duckCount)
			throw new ArgumentOutOfRangeException(nameof(duck), duck.Index, "No such duck in this engine.");
	}

	// Whether `bus` is mixed, directly or through other buses, into `ancestor`
	private bool Contains(int ancestor, int bus)
	{
		for (; bus != 0; bus = _busParents[bus])
			if (_busParents[bus] == ancestor)
				return true;
		return false;
	}

	private static void CheckVolume(float volume)
	{
		if (!float.IsFinite(volume) || volume < 0f)
			throw new ArgumentOutOfRangeException(nameof(volume), volume, "A volume is a linear gain, zero or more.");
	}

	private static void CheckPitch(float pitch)
	{
		if (!float.IsFinite(pitch) || pitch <= 0f)
			throw new ArgumentOutOfRangeException(nameof(pitch), pitch, "A pitch is more than zero: 1 plays as recorded.");
	}

	private static void CheckPan(float pan)
	{
		if (!float.IsFinite(pan) || pan < -1f || pan > 1f)
			throw new ArgumentOutOfRangeException(nameof(pan), pan, "A pan runs from -1 (left) to 1 (right).");
	}
}
