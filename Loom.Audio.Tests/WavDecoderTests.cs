using System.Buffers.Binary;

using Loom.Audio.Decoding;

namespace Loom.Tests;

/// <summary>Reading WAV files: the sample formats, the chunk walk, and the files that aren't WAV.</summary>
public class WavDecoderTests
{
	private static byte[] Wav(int format, int channels, int rate, int bits, byte[] data, int? dataSize = null, bool extraChunk = false)
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write("RIFF"u8);
		writer.Write(0); // the RIFF size, which a reader doesn't need
		writer.Write("WAVE"u8);
		if (extraChunk)
		{
			writer.Write("LIST"u8);
			writer.Write(3);
			writer.Write(new byte[] { 1, 2, 3, 0 }); // an odd-sized chunk, padded
		}
		writer.Write("fmt "u8);
		writer.Write(16);
		writer.Write((ushort)format);
		writer.Write((ushort)channels);
		writer.Write(rate);
		writer.Write(rate * channels * bits / 8);
		writer.Write((ushort)(channels * bits / 8));
		writer.Write((ushort)bits);
		writer.Write("data"u8);
		writer.Write(dataSize ?? data.Length);
		writer.Write(data);
		return stream.ToArray();
	}

	[Test]
	public void Sixteen_bit_mono_PCM_reads_as_minus_one_to_one()
	{
		var data = new byte[6];
		BinaryPrimitives.WriteInt16LittleEndian(data, short.MinValue);
		BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2), 0);
		BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 16384);

		var (samples, channels, rate) = WavDecoder.Decode(Wav(1, 1, 22_050, 16, data, extraChunk: true));

		channels.ShouldBe(1);
		rate.ShouldBe(22_050);
		samples.ShouldBe([-1f, 0f, 0.5f]);
	}

	[Test]
	public void Twenty_four_bit_stereo_PCM_keeps_its_sign()
	{
		byte[] data = [0x00, 0x00, 0x80, 0xFF, 0xFF, 0x7F]; // -8388608, 8388607

		var (samples, channels, _) = WavDecoder.Decode(Wav(1, 2, 48_000, 24, data));

		channels.ShouldBe(2);
		samples[0].ShouldBe(-1f);
		samples[1].ShouldBe(1f, 1e-6f);
	}

	[Test]
	public void Float_samples_read_as_they_are()
	{
		var data = new byte[8];
		BinaryPrimitives.WriteSingleLittleEndian(data, 0.25f);
		BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), -0.75f);

		var (samples, _, _) = WavDecoder.Decode(Wav(3, 1, 44_100, 32, data));

		samples.ShouldBe([0.25f, -0.75f]);
	}

	[Test]
	public void A_data_chunk_claiming_more_than_the_file_holds_reads_what_is_there()
	{
		var data = new byte[4];

		var (samples, _, _) = WavDecoder.Decode(Wav(1, 1, 8_000, 16, data, dataSize: int.MaxValue));

		samples.Length.ShouldBe(2);
	}

	[Test]
	public void Bytes_that_are_not_a_WAV_file_are_refused()
	{
		Should.Throw<InvalidDataException>(() => WavDecoder.Decode("OggS not a wav"u8));
	}

	[Test]
	public void An_unsupported_sample_format_says_which_it_was()
	{
		var thrown = Should.Throw<NotSupportedException>(() => WavDecoder.Decode(Wav(2, 1, 8_000, 4 * 8, new byte[8])));

		thrown.Message.ShouldContain("format 2");
	}
}
