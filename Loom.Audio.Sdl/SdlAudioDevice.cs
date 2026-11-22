using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Loom.Audio.Sdl;

/// <summary>
/// The desktop's audio output, through SDL 3: the system's default playback device, opened at its own rate in 32-bit
/// float stereo. SDL calls back on its own audio thread for each buffer, the mixer renders into a buffer of ours there,
/// and SDL copies it out. A stream on the default device follows the system's default as it changes (headphones
/// plugged in), which SDL does on its own.
/// </summary>
internal sealed unsafe class SdlAudioDevice : IAudioDevice
{
	// The buffer asked of SDL when the app leaves it to the backend: small, for latency. SDL rounds it to what the
	// platform's audio API will take, and the device's format says what it got.
	private const int LowestBufferFrames = 128;
	private const int ChunkFrames = 2048;

	private readonly float[] _buffer = new float[ChunkFrames * 2];
	private GCHandle _self;
	private nint _stream;
	private bool _subsystemStarted;
	private IAudioRenderer? _renderer;
	private bool _disposed;

	private SdlAudioDevice() { }

	public string Backend => "SDL";

	public string Name { get; private set; } = "";

	public int SampleRate { get; private set; }

	public int BufferFrames { get; private set; }

	/// <summary>Opens the default playback device for <paramref name="options"/>, or returns null with the reason where SDL3 or a device is missing.</summary>
	public static SdlAudioDevice? TryOpen(AudioOptions options, out string reason)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (!Sdl3.TryLoad(out reason))
			return null;
		try
		{
			return Open(options, out reason);
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
		{
			reason = $"SDL3.dll would not load: {ex.Message}";
			return null;
		}
	}

	private static SdlAudioDevice? Open(AudioOptions options, out string reason)
	{
		// Read as the device opens. No Ctrl+C handler of SDL's (see SdlGamepadPump); the buffer; and the stream's role,
		// which tells the platform's mixer this is a game's sound
		var frames = options.BufferFrames > 0 ? options.BufferFrames : LowestBufferFrames;
		Sdl3.Hint("SDL_NO_SIGNAL_HANDLERS", "1");
		Sdl3.Hint("SDL_AUDIO_DEVICE_SAMPLE_FRAMES", frames.ToString(CultureInfo.InvariantCulture));
		Sdl3.Hint("SDL_AUDIO_DEVICE_STREAM_ROLE", "Game");

		var device = new SdlAudioDevice();
		try
		{
			if (Sdl3.SDL_InitSubSystem(Sdl3.InitAudio) == 0)
			{
				reason = $"SDL's audio would not start: {Sdl3.Text(Sdl3.SDL_GetError())}";
				device.Dispose();
				return null;
			}
			device._subsystemStarted = true;

			SdlAudioSpec native;
			int nativeFrames;
			if (Sdl3.SDL_GetAudioDeviceFormat(Sdl3.AudioDeviceDefaultPlayback, &native, &nativeFrames) == 0)
			{
				reason = $"no playback device: {Sdl3.Text(Sdl3.SDL_GetError())}";
				device.Dispose();
				return null;
			}

			// The device's own rate unless the app asked for another: any other makes the platform resample
			var spec = new SdlAudioSpec { Format = Sdl3.AudioF32, Channels = 2, Freq = options.SampleRate > 0 ? options.SampleRate : native.Freq };
			device._self = GCHandle.Alloc(device);
			device._stream = Sdl3.SDL_OpenAudioDeviceStream(Sdl3.AudioDeviceDefaultPlayback, &spec, &OnAudio, GCHandle.ToIntPtr(device._self));
			if (device._stream == 0)
			{
				reason = $"SDL would not open the playback device: {Sdl3.Text(Sdl3.SDL_GetError())}";
				device.Dispose();
				return null;
			}

			var id = Sdl3.SDL_GetAudioStreamDevice(device._stream);
			SdlAudioSpec granted;
			int grantedFrames;
			device.BufferFrames = Sdl3.SDL_GetAudioDeviceFormat(id, &granted, &grantedFrames) != 0 ? grantedFrames : 0;
			device.SampleRate = spec.Freq;
			device.Name = Sdl3.Text(Sdl3.SDL_GetAudioDeviceName(id));
			reason = "";
			return device;
		}
		catch
		{
			device.Dispose();
			throw;
		}
	}

	public void Start(IAudioRenderer renderer)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_renderer is not null)
			throw new InvalidOperationException("The device has already been started.");
		Volatile.Write(ref _renderer, renderer);
		Resume(); // SDL opens a device stream paused
	}

	public void Pause()
	{
		if (_stream != 0 && Sdl3.SDL_PauseAudioStreamDevice(_stream) == 0)
			throw new InvalidOperationException($"SDL would not pause the playback device: {Sdl3.Text(Sdl3.SDL_GetError())}");
	}

	public void Resume()
	{
		if (_stream != 0 && Sdl3.SDL_ResumeAudioStreamDevice(_stream) == 0)
			throw new InvalidOperationException($"SDL would not start the playback device: {Sdl3.Text(Sdl3.SDL_GetError())}");
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		// Closes the device too (the stream was opened with it); SDL holds the stream's lock while it calls back, so no
		// callback runs after this returns
		if (_stream != 0)
		{
			Sdl3.SDL_DestroyAudioStream(_stream);
			_stream = 0;
		}
		if (_self.IsAllocated)
			_self.Free();
		if (_subsystemStarted)
		{
			_subsystemStarted = false;
			Sdl3.QuitSubSystem(Sdl3.InitAudio);
		}
	}

	// SDL's audio thread: the device wants at least `additional` more bytes queued on the stream
	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static void OnAudio(nint self, nint stream, int additional, int total)
	{
		if (additional <= 0)
			return;
		var device = (SdlAudioDevice)GCHandle.FromIntPtr(self).Target!;
		device.Fill(stream, (additional + sizeof(float) * 2 - 1) / (sizeof(float) * 2));
	}

	private void Fill(nint stream, int frames)
	{
		var renderer = Volatile.Read(ref _renderer);
		while (frames > 0)
		{
			var n = Math.Min(frames, ChunkFrames);
			var samples = _buffer.AsSpan(0, n * 2);
			if (renderer is null)
				samples.Clear();
			else
				renderer.Render(samples);
			fixed (float* p = _buffer)
				Sdl3.SDL_PutAudioStreamData(stream, p, n * 2 * sizeof(float));
			frames -= n;
		}
	}
}
