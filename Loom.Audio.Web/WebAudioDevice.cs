using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Loom.Audio.Web;

/// <summary>
/// The browser's audio output: an AudioWorklet (<c>loom-audio.js</c>) the mix is sent to once a frame. The mixer can't
/// run in the worklet (the .NET runtime doesn't live on the browser's audio thread), so it runs here, on the frame's
/// thread, a lead of <see cref="BufferFrames"/> ahead of the speaker: enough to ride out a late frame. That lead is the
/// web's latency, by design, where the desktop and Android render on the audio thread itself.
/// </summary>
/// <remarks>
/// A browser lets a page make sound only after the player's first click, tap or key; until then the device keeps the mix's
/// time (voices advance and end) and sends nothing.
/// </remarks>
[SupportedOSPlatform("browser")]
internal sealed partial class WebAudioDevice : IAudioDevice
{
	internal const string Module = "loom-audio";
	private const int DefaultRate = 48_000;
	private const int DefaultLeadFrames = 2_400; // 50 ms at 48 kHz: three frames of a 60 Hz display
	private const int ChunkFrames = 1024;

	private readonly float[] _chunk = new float[ChunkFrames * 2];
	private IAudioRenderer? _renderer;
	private double _owed; // frames of mix time to keep while the output can't be heard
	private bool _paused;
	private bool _disposed;

	public WebAudioDevice(AudioOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		SampleRate = AudioOpen(options.SampleRate > 0 ? options.SampleRate : DefaultRate);
		BufferFrames = options.BufferFrames > 0 ? options.BufferFrames : SampleRate * DefaultLeadFrames / DefaultRate;
	}

	public string Backend => "WebAudio";

	public string Name => "AudioWorklet";

	public int SampleRate { get; }

	public int BufferFrames { get; }

	public void Start(IAudioRenderer renderer)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		if (_renderer is not null)
			throw new InvalidOperationException("The device has already been started.");
		_renderer = renderer;
	}

	public void Pause()
	{
		_paused = true;
		AudioPause();
	}

	public void Resume()
	{
		_paused = false;
		AudioResume();
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		AudioClose();
	}

	/// <summary>
	/// <see cref="AudioEngine.Update"/>'s once-a-frame call: tops the worklet's queue up to the lead, or, while nothing can
	/// be heard, renders the frame's worth of mix and drops it so the mix keeps time.
	/// </summary>
	public void Pump(double deltaSeconds)
	{
		if (_disposed || _paused || _renderer is not { } renderer)
			return;
		var wanted = AudioFramesWanted(BufferFrames);
		if (wanted < 0)
		{
			// At most a quarter second at once: a long stall (a background tab) is time the mix skips, not renders
			_owed = Math.Min(_owed + deltaSeconds * SampleRate, SampleRate / 4.0);
			var frames = (int)_owed;
			_owed -= frames;
			Render(renderer, frames, send: false);
			return;
		}
		_owed = 0;
		Render(renderer, wanted, send: true);
	}

	private void Render(IAudioRenderer renderer, int frames, bool send)
	{
		while (frames > 0)
		{
			var n = Math.Min(frames, ChunkFrames);
			var samples = _chunk.AsSpan(0, n * 2);
			renderer.Render(samples);
			if (send)
				AudioWrite(MemoryMarshal.AsBytes(samples));
			frames -= n;
		}
	}

	[JSImport("audioOpen", Module)]
	private static partial int AudioOpen(int sampleRate);

	[JSImport("audioFramesWanted", Module)]
	private static partial int AudioFramesWanted(int leadFrames);

	[JSImport("audioWrite", Module)]
	private static partial void AudioWrite([JSMarshalAs<JSType.MemoryView>] Span<byte> samples);

	[JSImport("audioPause", Module)]
	private static partial void AudioPause();

	[JSImport("audioResume", Module)]
	private static partial void AudioResume();

	[JSImport("audioClose", Module)]
	private static partial void AudioClose();
}
