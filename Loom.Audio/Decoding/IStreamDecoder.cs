namespace Loom.Audio.Decoding;

/// <summary>
/// A compressed file decoded a piece at a time, into interleaved 32-bit float: what an <see cref="AudioStream"/> plays and
/// what a clip is decoded whole from. Once made it allocates nothing, so a stream can be decoded on any thread.
/// </summary>
internal interface IStreamDecoder
{
	int Channels { get; }

	/// <summary>The file's rate, in frames per second.</summary>
	int SampleRate { get; }

	/// <summary>
	/// Decodes up to <paramref name="destination"/>'s length in interleaved samples; returns the frames written, fewer only
	/// at the end of the stream.
	/// </summary>
	/// <exception cref="InvalidDataException">The file is damaged.</exception>
	int Read(Span<float> destination);

	/// <summary>Goes back to the stream's first sample. The stream must be seekable.</summary>
	void Rewind();
}
