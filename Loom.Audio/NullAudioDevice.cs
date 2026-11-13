using System.Diagnostics;

namespace Loom.Audio;

/// <summary>
/// A device with no output, for a host or machine that has none: it renders in real time on a thread of its own and
/// drops what it renders. The mix still runs, so voices advance and end, sources run, and a game that waits on a sound
/// finishing behaves as it would with a speaker.
/// </summary>
public sealed class NullAudioDevice : IAudioDevice
{
	private const int BlockFrames = 480;
	private readonly float[] _block = new float[BlockFrames * 2];
	private IAudioRenderer? _renderer;
	private Thread? _thread;
	private volatile bool _paused;
	private volatile bool _disposed;

	/// <summary>A silent device at <paramref name="sampleRate"/>.</summary>
	/// <param name="sampleRate">The rate to render at, in frames per second.</param>
	/// <param name="reason">Why there is no real output, reported as the device's <see cref="Name"/>.</param>
	public NullAudioDevice(int sampleRate = 48_000, string reason = "no audio output")
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		SampleRate = sampleRate;
		Name = reason;
	}

	/// <inheritdoc />
	public string Backend => "Null";

	/// <inheritdoc />
	public string Name { get; }

	/// <inheritdoc />
	public int SampleRate { get; }

	/// <inheritdoc />
	public int BufferFrames => BlockFrames;

	/// <inheritdoc />
	public void Start(IAudioRenderer renderer)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		if (_renderer is not null)
			throw new InvalidOperationException("The device has already been started.");
		_renderer = renderer;
		_thread = new Thread(Run) { IsBackground = true, Name = "Loom null audio" };
		_thread.Start();
	}

	/// <inheritdoc />
	public void Pause() => _paused = true;

	/// <inheritdoc />
	public void Resume() => _paused = false;

	/// <inheritdoc />
	public void Dispose()
	{
		_disposed = true;
		_thread?.Join();
	}

	private void Run()
	{
		var renderer = _renderer!;
		var start = Stopwatch.GetTimestamp();
		long rendered = 0;
		while (!_disposed)
		{
			if (_paused)
			{
				Thread.Sleep(10);
				start = Stopwatch.GetTimestamp();
				rendered = 0;
				continue;
			}
			var due = (long)(Stopwatch.GetElapsedTime(start).TotalSeconds * SampleRate);
			// Behind by more than a second (a debugger stop): skip ahead rather than render the whole gap at once
			if (due - rendered > SampleRate)
				rendered = due - BlockFrames;
			while (rendered + BlockFrames <= due && !_disposed)
			{
				renderer.Render(_block);
				rendered += BlockFrames;
			}
			Thread.Sleep(2);
		}
	}
}
