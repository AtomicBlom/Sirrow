using System.Runtime.CompilerServices;

using Loom.Audio.Decoding;
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
	private bool _disposed;

	/// <summary>An engine playing through <paramref name="device"/>, which it starts and from then on owns (disposing it with itself).</summary>
	public AudioEngine(IAudioDevice device, AudioOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(device);
		options ??= new AudioOptions();
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxVoices, nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxBuses, nameof(options));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.CommandCapacity, nameof(options));
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

		_mixer = new AudioMixer(SampleRate, options.MaxVoices, options.MaxBuses, _commands, _ended);
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
	/// <param name="source">A source not already playing.</param>
	/// <param name="bus">Where the voice is mixed: the master bus by default.</param>
	/// <param name="volume">A linear gain: 1 plays the source as it renders.</param>
	/// <param name="pan">-1 full left, 0 centre, 1 full right: a balance, since a source is stereo.</param>
	public VoiceHandle Play(AudioSource source, AudioBus bus = default, float volume = 1f, float pan = 0f)
	{
		CheckThread();
		ArgumentNullException.ThrowIfNull(source);
		CheckBus(bus);
		CheckVolume(volume);
		CheckPan(pan);
		if (source.IsPlaying)
			throw new InvalidOperationException("The source is already playing: a source plays on one voice at a time. Stop it first, or make another.");
		source.Start(SampleRate);
		if (!TryTakeVoice(out var slot, out var generation))
			return VoiceHandle.None;
		_sources[slot] = source;
		source.VoiceSlot = slot;
		StartVoice(slot, new AudioCommand { Kind = AudioCommandKind.PlaySource, Index = slot, Bus = bus.Index, A = volume, C = pan, Payload = source });
		return new VoiceHandle(slot, generation);
	}

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
	public void SetPan(VoiceHandle voice, float pan)
	{
		CheckThread();
		CheckPan(pan);
		if (IsLive(voice))
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

	/// <summary>Stops <paramref name="source"/>'s voice, if it is playing (see <see cref="Stop(VoiceHandle, float)"/>).</summary>
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
	/// Hears from the audio thread: frees the voices that have ended, and throws what a source threw there. A
	/// <c>LoomApplication</c> calls it once a frame; an engine used on its own should too.
	/// </summary>
	/// <exception cref="InvalidOperationException">An <see cref="AudioSource"/> threw on the audio thread (the inner exception); its voice was stopped.</exception>
	public void Update()
	{
		CheckThread();
		TakeEnded();
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

	/// <summary>Stops and closes the device.</summary>
	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		Device.Dispose();
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
