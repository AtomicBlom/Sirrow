namespace BinaryVibrance.Sirrow;

/// <summary>
/// Decoded sound at its file's own rate, held by no engine: what <see cref="AudioDecoder"/> gives, for code that keeps
/// samples itself (a synthesizer's sample player, a tool).
/// </summary>
/// <param name="Samples">The samples, -1..1, interleaved when stereo.</param>
/// <param name="Channels">1 or 2.</param>
/// <param name="SampleRate">The samples' rate, in frames per second.</param>
public sealed record AudioData(float[] Samples, int Channels, int SampleRate)
{
	/// <summary>How many frames it holds: samples per channel.</summary>
	public int Frames => Samples.Length / Channels;
}
