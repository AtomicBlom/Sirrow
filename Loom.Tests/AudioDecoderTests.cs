using Loom.Audio;
using Loom.Audio.Synth;

namespace Loom.Tests;

/// <summary>Decoding without an engine: WAV, Ogg Vorbis and FLAC to samples at the file's own rate.</summary>
public class AudioDecoderTests
{
	private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "AudioFixtures", name));

	[Test]
	public void A_wav_decodes_to_the_samples_written()
	{
		var samples = new float[2000];
		for (var i = 0; i < samples.Length; i++)
			samples[i] = MathF.Sin(i * 0.05f) * 0.5f;
		using var stream = new MemoryStream();
		WavWriter.Write(stream, samples, channels: 2, sampleRate: 44_100);

		var data = AudioDecoder.DecodeWav(stream.ToArray());

		data.Channels.ShouldBe(2);
		data.SampleRate.ShouldBe(44_100);
		data.Frames.ShouldBe(1000);
		data.Samples.ShouldBe(samples);
	}

	[Test]
	public void An_ogg_decodes_to_what_the_engine_loads_at_its_own_rate()
	{
		var ogg = Fixture("stereo-44k.ogg");
		var data = AudioDecoder.Decode(ogg);
		using var engine = new AudioEngine(new OfflineAudioDevice(data.SampleRate, 256));

		var clip = engine.LoadOgg(ogg);

		data.Channels.ShouldBe(2);
		data.SampleRate.ShouldBe(44_100);
		clip.Samples.ShouldBe(data.Samples);
	}

	[Test]
	public void A_flac_decodes_to_what_the_engine_loads_at_its_own_rate()
	{
		var flac = Fixture("flac-stereo-44k.flac");
		var data = AudioDecoder.Decode(flac);
		using var engine = new AudioEngine(new OfflineAudioDevice(data.SampleRate, 256));

		var clip = engine.LoadFlac(flac);

		data.Channels.ShouldBe(2);
		data.SampleRate.ShouldBe(44_100);
		clip.Samples.ShouldBe(data.Samples);
	}

	[Test]
	public void Bytes_that_are_neither_throw() =>
		Should.Throw<InvalidDataException>(() => AudioDecoder.Decode("abcd"u8));
}
