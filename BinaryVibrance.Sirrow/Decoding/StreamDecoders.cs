using BinaryVibrance.Sirrow.Decoding.Flac;
using BinaryVibrance.Sirrow.Decoding.Vorbis;

namespace BinaryVibrance.Sirrow.Decoding;

/// <summary>Picks the decoder for a file by its first bytes.</summary>
internal static class StreamDecoders
{
	/// <summary>A decoder for <paramref name="stream"/>, which must be seekable: Ogg Vorbis or FLAC.</summary>
	/// <exception cref="InvalidDataException">The file is neither, or is damaged.</exception>
	/// <exception cref="NotSupportedException">The file uses a feature the decoder refuses.</exception>
	public static IStreamDecoder Open(Stream stream)
	{
		Span<byte> head = stackalloc byte[4];
		var start = stream.Position;
		var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
		stream.Position = start;
		head = head[..read];
		if (head.SequenceEqual("OggS"u8))
			return new VorbisDecoder(stream);
		if (head.SequenceEqual("fLaC"u8) || head.StartsWith("ID3"u8)) // an ID3 tag some taggers put before the fLaC
			return new FlacDecoder(stream);
		throw new InvalidDataException("This is neither Ogg Vorbis nor FLAC: it starts with neither OggS nor fLaC.");
	}
}
