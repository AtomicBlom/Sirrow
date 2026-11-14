using Loom.Audio.Decoding.Vorbis;

namespace Loom.Tests;

/// <summary>
/// The Ogg Vorbis decoder against libvorbis, the reference decoder: files libvorbis encoded, decoded by both and compared
/// sample by sample (<c>AudioFixtures/make.ps1</c> makes them). Then the decoder's own rules (rewinding, damaged files,
/// no allocation), and the transform it rests on, against its formula.
/// </summary>
public class VorbisDecoderTests
{
	private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "AudioFixtures", name);

	private static VorbisDecoder Open(string name) => new(new MemoryStream(File.ReadAllBytes(Fixture(name + ".ogg"))));

	private static float[] DecodeAll(VorbisDecoder decoder)
	{
		var samples = new List<float>();
		var chunk = new float[1000 * decoder.Channels];
		int frames;
		while ((frames = decoder.Read(chunk)) > 0)
			samples.AddRange(chunk.AsSpan(0, frames * decoder.Channels));
		return [.. samples];
	}

	private static float[] Reference(string name)
	{
		var bytes = File.ReadAllBytes(Fixture(name + ".s16"));
		var samples = new float[bytes.Length / 2];
		for (var i = 0; i < samples.Length; i++)
			samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
		return samples;
	}

	[Test]
	[Arguments("stereo-44k", 2, 44_100, 22_050)]
	[Arguments("mono-48k-q0", 1, 48_000, 19_200)]
	[Arguments("surround-48k", 6, 48_000, 9_600)]
	public void Decodes_as_libvorbis_does_to_the_last_sample_encoded(string name, int channels, int rate, int sourceFrames)
	{
		var decoder = Open(name);
		var samples = DecodeAll(decoder);
		var reference = Reference(name);

		decoder.Channels.ShouldBe(channels);
		decoder.SampleRate.ShouldBe(rate);
		// Exactly what was encoded, as the last page's granule position says. (ffmpeg's decode, the reference, stops a
		// short block's half early, which is ffmpeg's trimming, not the stream's.)
		(samples.Length / channels).ShouldBe(sourceFrames);
		var maxError = 0f;
		for (var i = 0; i < reference.Length; i++)
			maxError = MathF.Max(maxError, MathF.Abs(samples[i] - reference[i]));
		maxError.ShouldBeLessThan(2e-5f); // the reference's own rounding to 16 bits is 1.5e-5
	}

	[Test]
	public void A_rewound_decoder_decodes_the_same_again()
	{
		var decoder = Open("stereo-44k");
		var first = DecodeAll(decoder);

		decoder.Rewind();
		var second = DecodeAll(decoder);

		second.ShouldBe(first);
	}

	[Test]
	public void Reads_of_any_size_join_up_to_the_same_samples()
	{
		var whole = DecodeAll(Open("stereo-44k"));
		var decoder = Open("stereo-44k");
		var pieces = new List<float>();
		var sizes = new[] { 1, 7, 128, 333, 2048 };
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
	public void A_damaged_page_fails_its_checksum()
	{
		var bytes = File.ReadAllBytes(Fixture("stereo-44k.ogg"));
		bytes[^200] ^= 0x10; // in the audio, past the headers

		Should.Throw<InvalidDataException>(() => DecodeAll(new VorbisDecoder(new MemoryStream(bytes)))).Message.ShouldContain("checksum");
	}

	[Test]
	public void A_file_cut_short_inside_a_page_throws()
	{
		var bytes = File.ReadAllBytes(Fixture("stereo-44k.ogg"));
		var decoder = new VorbisDecoder(new MemoryStream(bytes, 0, bytes.Length - 100));

		Should.Throw<InvalidDataException>(() => DecodeAll(decoder)).Message.ShouldContain("cut short");
	}

	[Test]
	public void Bytes_that_are_not_Ogg_Vorbis_throw()
	{
		Should.Throw<InvalidDataException>(() => new VorbisDecoder(new MemoryStream("RIFF\0\0\0\0WAVEfmt "u8.ToArray())));
		Should.Throw<InvalidDataException>(() => new VorbisDecoder(new MemoryStream([])));
	}

	[Test]
	public void Decoding_allocates_nothing_once_the_headers_are_read()
	{
		var decoder = Open("stereo-44k");
		var chunk = new float[512 * 2];
		decoder.Read(chunk); // the packet buffer reaches its size on the first packets

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

	[Test]
	[Arguments(64)]
	[Arguments(256)]
	[Arguments(2048)]
	public void The_fast_IMDCT_matches_its_formula(int n)
	{
		var random = new Random(n);
		var spectrum = new float[n / 2];
		for (var k = 0; k < spectrum.Length; k++)
			spectrum[k] = random.NextSingle() * 2f - 1f;
		var fast = new float[n];

		new Imdct(n).Inverse(spectrum, fast);

		for (var i = 0; i < n; i++)
		{
			var direct = 0.0;
			for (var k = 0; k < n / 2; k++)
				direct += spectrum[k] * Math.Cos(2 * Math.PI / n * (i + 0.5 + n / 4.0) * (k + 0.5));
			fast[i].ShouldBe((float)direct, 1e-3f * Math.Max(1f, MathF.Sqrt(n)));
		}
	}
}
