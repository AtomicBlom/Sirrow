namespace BinaryVibrance.Sirrow;

/// <summary>What an <see cref="AudioSource"/> is rendering: the block's length, the engine's rate and where the block falls on the engine's clock.</summary>
/// <param name="sampleRate">The engine's rate, in frames per second.</param>
/// <param name="frames">How many frames the block holds.</param>
/// <param name="frameTime">How many frames the engine had rendered before this block.</param>
public readonly struct AudioRenderInfo(int sampleRate, int frames, long frameTime)
{
	/// <summary>The engine's rate, in frames per second.</summary>
	public int SampleRate { get; } = sampleRate;

	/// <summary>How many frames the block holds: the length of each span the source fills.</summary>
	public int Frames { get; } = frames;

	/// <summary>
	/// How many frames the engine had rendered before this block: the block's start on the engine's sample clock, which
	/// runs only while the device does. A sequencer counts beats on it.
	/// </summary>
	public long FrameTime { get; } = frameTime;
}
