using System.Security.Cryptography;

using Loom.Audio.Decoding.Flac;

namespace Loom.Tests;

/// <summary>
/// The FLAC decoder, bit for bit: every file under <c>AudioFixtures</c> (<c>make.ps1</c> makes them, each forcing a
/// different corner of the format) is decoded and checked against the MD5 its encoder took of the samples. Then the
/// float output, the decoder's own rules (rewinding, tags around the audio, damaged files, no allocation).
/// </summary>
public class FlacDecoderTests
{
	private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "AudioFixtures", name);

	private static FlacDecoder Open(string name) => new(new MemoryStream(File.ReadAllBytes(Fixture(name + ".flac"))));

	/// <summary>Every FLAC file under the fixtures: the committed ones, and any more put there for a one-off run.</summary>
	public static IEnumerable<string> Files() =>
		Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "AudioFixtures"), "*.flac", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "AudioFixtures"), path)[..^".flac".Length])
			.Order();

	private static float[] DecodeAll(FlacDecoder decoder)
	{
		var samples = new List<float>();
		var chunk = new float[1000 * decoder.Channels];
		int frames;
		while ((frames = decoder.Read(chunk)) > 0)
			samples.AddRange(chunk.AsSpan(0, frames * decoder.Channels));
		return [.. samples];
	}

	[Test]
	[MethodDataSource(nameof(Files))]
	public void Decodes_exactly_the_samples_its_MD5_was_taken_of(string name)
	{
		var decoder = Open(name);
		decoder.Md5.ShouldNotBe(new byte[16], "the file has no MD5 to check against");
		using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		var bytes = new byte[4];
		var frames = 0L;

		while (decoder.DecodeFrame())
		{
			// As the MD5 was taken: each sample little-endian in as few whole bytes as hold it, channels interleaved
			var width = (decoder.FrameBits + 7) / 8;
			for (var i = 0; i < decoder.FrameLength; i++)
			{
				for (var c = 0; c < decoder.Channels; c++)
				{
					var sample = decoder.Samples[c][i];
					for (var b = 0; b < width; b++)
						bytes[b] = (byte)(sample >> (8 * b));
					md5.AppendData(bytes, 0, width);
				}
			}
			frames += decoder.FrameLength;
		}

		md5.GetHashAndReset().ShouldBe(decoder.Md5);
		if (decoder.TotalFrames > 0)
			frames.ShouldBe(decoder.TotalFrames);
	}

	[Test]
	[Arguments("flac-stereo-44k", 2, 44_100, 16, 11_025)]
	[Arguments("flac-24bit-lpc32", 2, 48_000, 24, 9_600)]
	[Arguments("flac-mid-side", 2, 22_050, 16, 6_615)]
	[Arguments("flac-noise", 1, 44_100, 16, 2_205)]
	[Arguments("flac-wasted-bits", 2, 48_000, 24, 9_600)]
	[Arguments("flac-surround", 6, 48_000, 16, 4_800)]
	public void Reads_the_format_from_the_metadata(string name, int channels, int rate, int bits, int frames)
	{
		var decoder = Open(name);

		decoder.Channels.ShouldBe(channels);
		decoder.SampleRate.ShouldBe(rate);
		decoder.BitsPerSample.ShouldBe(bits);
		decoder.TotalFrames.ShouldBe(frames);
		(DecodeAll(decoder).Length / channels).ShouldBe(frames);
	}

	[Test]
	[Arguments("flac-stereo-44k")]
	[Arguments("flac-24bit-lpc32")]
	public void Plays_each_sample_scaled_to_full_scale_at_one(string name)
	{
		var floats = DecodeAll(Open(name));
		var decoder = Open(name);
		var expected = new List<float>();
		while (decoder.DecodeFrame())
		{
			for (var i = 0; i < decoder.FrameLength; i++)
			{
				for (var c = 0; c < decoder.Channels; c++)
					expected.Add(decoder.Samples[c][i] / (float)(1 << (decoder.FrameBits - 1)));
			}
		}

		floats.ShouldBe([.. expected]);
		floats.Max(MathF.Abs).ShouldBeGreaterThan(0.2f); // and not silence
	}

	[Test]
	public void A_rewound_decoder_decodes_the_same_again()
	{
		var decoder = Open("flac-fixed-odd");
		var first = DecodeAll(decoder);

		decoder.Rewind();
		var second = DecodeAll(decoder);

		second.ShouldBe(first);
	}

	[Test]
	public void Reads_of_any_size_join_up_to_the_same_samples()
	{
		var whole = DecodeAll(Open("flac-stereo-44k"));
		var decoder = Open("flac-stereo-44k");
		var pieces = new List<float>();
		var sizes = new[] { 1, 7, 128, 333, 5000 };
		for (var i = 0; ; i++)
		{
			var chunk = new float[sizes[i % sizes.Length] * 2];
			var frames = decoder.Read(chunk);
			if (frames == 0)
				break;
			pieces.AddRange(chunk.AsSpan(0, frames * 2));
		}

		pieces.ToArray().ShouldBe(whole);
	}

	[Test]
	public void Tags_before_and_after_the_audio_are_skipped()
	{
		var flac = File.ReadAllBytes(Fixture("flac-stereo-44k.flac"));
		byte[] id3v2 = [.. "ID3"u8, 3, 0, 0, 0, 0, 0, 20, .. new byte[20]]; // an empty ID3v2.3 tag, its size seven bits a byte
		byte[] id3v1 = [.. "TAG"u8, .. new byte[125]];
		byte[] tagged = [.. id3v2, .. flac, .. id3v1];

		DecodeAll(new FlacDecoder(new MemoryStream(tagged))).ShouldBe(DecodeAll(Open("flac-stereo-44k")));
	}

	[Test]
	public void A_damaged_frame_fails_its_checksum()
	{
		var bytes = File.ReadAllBytes(Fixture("flac-stereo-44k.flac"));
		bytes[^2000] ^= 0x10; // in a frame's body

		Should.Throw<InvalidDataException>(() => DecodeAll(new FlacDecoder(new MemoryStream(bytes)))).Message.ShouldContain("checksum");
	}

	[Test]
	public void A_file_cut_short_inside_a_frame_throws()
	{
		var bytes = File.ReadAllBytes(Fixture("flac-stereo-44k.flac"));
		var decoder = new FlacDecoder(new MemoryStream(bytes, 0, bytes.Length - 100));

		Should.Throw<InvalidDataException>(() => DecodeAll(decoder)).Message.ShouldContain("cut short");
	}

	[Test]
	public void Bytes_that_are_not_FLAC_throw()
	{
		Should.Throw<InvalidDataException>(() => new FlacDecoder(new MemoryStream("RIFF\0\0\0\0WAVEfmt "u8.ToArray())));
		Should.Throw<InvalidDataException>(() => new FlacDecoder(new MemoryStream("OggS\0\0\0\0"u8.ToArray())));
		Should.Throw<InvalidDataException>(() => new FlacDecoder(new MemoryStream([])));
	}

	[Test]
	public void Decoding_allocates_nothing_once_the_metadata_is_read()
	{
		var decoder = Open("flac-24bit-lpc32");
		var chunk = new float[512 * 2];

		var before = GC.GetAllocatedBytesForCurrentThread();
		while (decoder.Read(chunk) > 0)
		{
		}
		decoder.Rewind();
		while (decoder.Read(chunk) > 0)
		{
		}
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		allocated.ShouldBe(0);
	}
}
