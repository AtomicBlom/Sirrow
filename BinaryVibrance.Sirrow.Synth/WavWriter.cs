using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace BinaryVibrance.Sirrow.Synth;

/// <summary>Writes samples as a WAV file of 32-bit float PCM, which every editor and Sirrow's own WAV reader open.</summary>
public static class WavWriter
{
	private const int HeaderBytes = 44;

	/// <summary>Writes <paramref name="samples"/> to <paramref name="stream"/> as a WAV file, header first.</summary>
	/// <param name="stream">Where the file's bytes go, from its current position.</param>
	/// <param name="samples">The samples, interleaved when <paramref name="channels"/> is 2.</param>
	/// <param name="channels">1 or 2.</param>
	/// <param name="sampleRate">The samples' rate, in frames per second.</param>
	/// <exception cref="ArgumentOutOfRangeException">The channels aren't 1 or 2, the rate isn't positive, or the samples don't make whole frames.</exception>
	public static void Write(Stream stream, ReadOnlySpan<float> samples, int channels, int sampleRate)
	{
		ArgumentNullException.ThrowIfNull(stream);
		Check(samples, channels, sampleRate);

		var dataBytes = checked(samples.Length * 4);
		Span<byte> header = stackalloc byte[HeaderBytes];
		"RIFF"u8.CopyTo(header);
		BinaryPrimitives.WriteInt32LittleEndian(header[4..], checked(36 + dataBytes));
		"WAVE"u8.CopyTo(header[8..]);
		"fmt "u8.CopyTo(header[12..]);
		BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
		BinaryPrimitives.WriteInt16LittleEndian(header[20..], 3); // IEEE float
		BinaryPrimitives.WriteInt16LittleEndian(header[22..], (short)channels);
		BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
		BinaryPrimitives.WriteInt32LittleEndian(header[28..], checked(sampleRate * channels * 4));
		BinaryPrimitives.WriteInt16LittleEndian(header[32..], (short)(channels * 4));
		BinaryPrimitives.WriteInt16LittleEndian(header[34..], 32);
		"data"u8.CopyTo(header[36..]);
		BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataBytes);
		stream.Write(header);

		if (BitConverter.IsLittleEndian)
		{
			stream.Write(MemoryMarshal.AsBytes(samples));
			return;
		}
		Span<byte> sample = stackalloc byte[4];
		foreach (var value in samples)
		{
			BinaryPrimitives.WriteSingleLittleEndian(sample, value);
			stream.Write(sample);
		}
	}

	/// <summary>Writes <paramref name="samples"/> to a WAV file at <paramref name="path"/>, making its folder if it is missing.</summary>
	/// <inheritdoc cref="Write(Stream, ReadOnlySpan{float}, int, int)"/>
	public static void Write(string path, ReadOnlySpan<float> samples, int channels, int sampleRate)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		// Checked before the file is made, so a bad call leaves no empty file behind
		Check(samples, channels, sampleRate);
		var folder = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(folder))
			Directory.CreateDirectory(folder);
		using var file = File.Create(path);
		Write(file, samples, channels, sampleRate);
	}

	private static void Check(ReadOnlySpan<float> samples, int channels, int sampleRate)
	{
		if (channels is not (1 or 2))
			throw new ArgumentOutOfRangeException(nameof(channels), channels, "A WAV file written here is mono or stereo.");
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		if (samples.Length % channels != 0)
			throw new ArgumentOutOfRangeException(nameof(samples), samples.Length, $"{samples.Length} samples don't make whole frames of {channels} channels.");
	}
}
