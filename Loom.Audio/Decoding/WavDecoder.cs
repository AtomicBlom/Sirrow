using System.Buffers.Binary;

namespace Loom.Audio.Decoding;

/// <summary>Reads a RIFF/WAVE file into float samples: PCM at 8, 16, 24 or 32 bits, and IEEE float at 32 or 64.</summary>
internal static class WavDecoder
{
	private const int FormatPcm = 1;
	private const int FormatFloat = 3;
	private const int FormatExtensible = 0xFFFE;

	/// <summary>The file's samples (interleaved, -1..1), channel count and rate.</summary>
	/// <exception cref="InvalidDataException">The bytes aren't a WAV file this can read.</exception>
	/// <exception cref="NotSupportedException">The file's sample format isn't one of those above.</exception>
	public static (float[] Samples, int Channels, int SampleRate) Decode(ReadOnlySpan<byte> wav)
	{
		if (wav.Length < 12 || !wav[..4].SequenceEqual("RIFF"u8) || !wav.Slice(8, 4).SequenceEqual("WAVE"u8))
			throw new InvalidDataException("Not a WAV file: it doesn't start with a RIFF/WAVE header.");

		int format = 0, channels = 0, rate = 0, bits = 0;
		var haveFormat = false;
		ReadOnlySpan<byte> data = default;
		var haveData = false;
		var position = 12;
		while (position <= wav.Length - 8)
		{
			var id = wav.Slice(position, 4);
			var size = BinaryPrimitives.ReadInt32LittleEndian(wav.Slice(position + 4, 4));
			var body = position + 8;
			if (size < 0)
				throw new InvalidDataException($"The WAV file's chunk at byte {position} has a negative size.");
			// A writer that never went back to fill in the size leaves the data chunk claiming more than the file holds
			var available = Math.Min(size, wav.Length - body);
			if (id.SequenceEqual("fmt "u8))
			{
				if (available < 16)
					throw new InvalidDataException("The WAV file's fmt chunk is too short.");
				var fmt = wav.Slice(body, available);
				format = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
				channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt[2..]);
				rate = BinaryPrimitives.ReadInt32LittleEndian(fmt[4..]);
				bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt[14..]);
				if (format == FormatExtensible && available >= 26)
					format = BinaryPrimitives.ReadUInt16LittleEndian(fmt[24..]); // the sub-format GUID starts with the plain format code
				haveFormat = true;
			}
			else if (id.SequenceEqual("data"u8))
			{
				data = wav.Slice(body, available);
				haveData = true;
			}
			var next = (long)body + size + (size & 1); // chunks are padded to an even length
			if (next > wav.Length)
				break;
			position = (int)next;
		}

		if (!haveFormat)
			throw new InvalidDataException("The WAV file has no fmt chunk.");
		if (!haveData)
			throw new InvalidDataException("The WAV file has no data chunk.");
		if (channels <= 0 || rate <= 0 || bits <= 0 || bits % 8 != 0)
			throw new InvalidDataException($"The WAV file's format is malformed: {channels} channels at {rate} Hz, {bits} bits.");

		var bytes = bits / 8;
		var count = data.Length / bytes;
		count -= count % channels;
		var samples = new float[count];
		switch (format, bits)
		{
			case (FormatPcm, 8):
				for (var i = 0; i < count; i++)
					samples[i] = (data[i] - 128) / 128f;
				break;
			case (FormatPcm, 16):
				for (var i = 0; i < count; i++)
					samples[i] = BinaryPrimitives.ReadInt16LittleEndian(data[(i * 2)..]) / 32768f;
				break;
			case (FormatPcm, 24):
				for (var i = 0; i < count; i++)
				{
					var o = i * 3;
					samples[i] = (data[o] | data[o + 1] << 8 | (sbyte)data[o + 2] << 16) / 8388608f;
				}
				break;
			case (FormatPcm, 32):
				for (var i = 0; i < count; i++)
					samples[i] = BinaryPrimitives.ReadInt32LittleEndian(data[(i * 4)..]) / 2147483648f;
				break;
			case (FormatFloat, 32):
				for (var i = 0; i < count; i++)
					samples[i] = BinaryPrimitives.ReadSingleLittleEndian(data[(i * 4)..]);
				break;
			case (FormatFloat, 64):
				for (var i = 0; i < count; i++)
					samples[i] = (float)BinaryPrimitives.ReadDoubleLittleEndian(data[(i * 8)..]);
				break;
			default:
				throw new NotSupportedException($"WAV sample format {format} at {bits} bits is not supported: PCM at 8, 16, 24 or 32 bits and float at 32 or 64 are.");
		}
		return (samples, channels, rate);
	}
}
