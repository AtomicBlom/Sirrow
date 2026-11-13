namespace Loom.Audio;

/// <summary>
/// A device that renders only when asked, on the caller's thread: for tests, for rendering to a file faster than real
/// time, and for a host that pumps audio itself. Nothing renders until <see cref="Render"/> is called.
/// </summary>
/// <param name="sampleRate">The rate to render at, in frames per second.</param>
/// <param name="bufferFrames">The buffer size to report, for code that sizes itself by the device's.</param>
public sealed class OfflineAudioDevice(int sampleRate = 48_000, int bufferFrames = 256) : IAudioDevice
{
	private IAudioRenderer? _renderer;

	/// <inheritdoc />
	public string Backend => "Offline";

	/// <inheritdoc />
	public string Name => "offline";

	/// <inheritdoc />
	public int SampleRate { get; } = sampleRate > 0 ? sampleRate : throw new ArgumentOutOfRangeException(nameof(sampleRate));

	/// <inheritdoc />
	public int BufferFrames { get; } = bufferFrames;

	/// <summary>Whether <see cref="Pause"/> has silenced the device.</summary>
	public bool IsPaused { get; private set; }

	/// <inheritdoc />
	public void Start(IAudioRenderer renderer)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		if (_renderer is not null)
			throw new InvalidOperationException("The device has already been started.");
		_renderer = renderer;
	}

	/// <summary>
	/// Renders the next <c>output.Length / 2</c> frames into <paramref name="output"/>, interleaved stereo, on the calling
	/// thread: silence while paused.
	/// </summary>
	public void Render(Span<float> output)
	{
		if (_renderer is null)
			throw new InvalidOperationException("The device has not been started: hand it to an AudioEngine first.");
		if (IsPaused)
			output.Clear();
		else
			_renderer.Render(output);
	}

	/// <inheritdoc />
	public void Pause() => IsPaused = true;

	/// <inheritdoc />
	public void Resume() => IsPaused = false;

	/// <inheritdoc />
	public void Dispose() { }
}
