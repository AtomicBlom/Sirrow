namespace Loom.Audio.Synth;

/// <summary>
/// An engine that renders only when asked, keeping what it rendered: for tests, and for rendering sound to a file. Play
/// sources and clips on <see cref="Engine"/>, then <see cref="Render"/> as much as is wanted; every call comes from the
/// thread that made the session, as every <see cref="AudioEngine"/> call does.
/// </summary>
public sealed class OfflineSession : IDisposable
{
	private const int BlockFrames = 256;

	private readonly OfflineAudioDevice _device;
	private readonly float[] _block = new float[BlockFrames * 2];
	private float[] _samples = new float[BlockFrames * 2 * 64];
	private int _count;

	/// <summary>A session rendering at <paramref name="sampleRate"/>, its engine made with <paramref name="options"/>.</summary>
	/// <param name="sampleRate">The rate to render at, in frames per second.</param>
	/// <param name="options">The engine's options: the defaults when null.</param>
	public OfflineSession(int sampleRate = 48_000, AudioOptions? options = null)
	{
		_device = new OfflineAudioDevice(sampleRate, BlockFrames);
		Engine = new AudioEngine(_device, options);
		SampleRate = sampleRate;
	}

	/// <summary>The engine to play on. The session owns it and disposes it.</summary>
	public AudioEngine Engine { get; }

	/// <summary>The rate the session renders at, in frames per second.</summary>
	public int SampleRate { get; }

	/// <summary>The frames rendered since the session began or was last <see cref="Clear">cleared</see>.</summary>
	public long Frames => _count / 2;

	/// <summary>Those frames, interleaved stereo.</summary>
	public ReadOnlySpan<float> Samples => _samples.AsSpan(0, _count);

	/// <summary>
	/// Renders the next <paramref name="frames"/> frames and keeps them, in blocks of at most 256 frames with the engine's
	/// <see cref="AudioEngine.Update"/> after each: a source that finishes is freed at the block it finished in, and one
	/// that throws is thrown again from the block it threw in.
	/// </summary>
	/// <param name="frames">How many frames, zero or more.</param>
	public void Render(int frames)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(frames);
		while (frames > 0)
		{
			var n = Math.Min(frames, BlockFrames);
			var block = _block.AsSpan(0, n * 2);
			_device.Render(block);
			Keep(block);
			Engine.Update();
			frames -= n;
		}
	}

	/// <summary>Renders <paramref name="seconds"/> of sound, rounded to the nearest frame.</summary>
	/// <param name="seconds">How long, zero or more.</param>
	public void RenderSeconds(double seconds)
	{
		if (!double.IsFinite(seconds) || seconds < 0)
			throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A length is zero or more seconds.");
		Render(checked((int)Math.Round(seconds * SampleRate)));
	}

	/// <summary>Forgets the frames kept. The engine plays on: the next render follows on from the last.</summary>
	public void Clear() => _count = 0;

	/// <summary>A copy of the left channel of <see cref="Samples"/>.</summary>
	public float[] Left() => Channel(0);

	/// <summary>A copy of the right channel of <see cref="Samples"/>.</summary>
	public float[] Right() => Channel(1);

	/// <summary>Saves <see cref="Samples"/> as a stereo WAV file of 32-bit floats (<see cref="WavWriter"/>).</summary>
	/// <param name="path">Where to write it; its folder is made if missing.</param>
	public void SaveWav(string path) => WavWriter.Write(path, Samples, 2, SampleRate);

	/// <summary>Disposes the engine.</summary>
	public void Dispose() => Engine.Dispose();

	private void Keep(ReadOnlySpan<float> block)
	{
		if (_count + block.Length > _samples.Length)
			Array.Resize(ref _samples, Math.Max(_samples.Length * 2, _count + block.Length));
		block.CopyTo(_samples.AsSpan(_count));
		_count += block.Length;
	}

	private float[] Channel(int channel)
	{
		var result = new float[_count / 2];
		for (var i = 0; i < result.Length; i++)
			result[i] = _samples[i * 2 + channel];
		return result;
	}
}
