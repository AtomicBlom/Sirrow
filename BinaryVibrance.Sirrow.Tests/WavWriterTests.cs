using System.Buffers.Binary;
using BinaryVibrance.Sirrow;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>WAV files of 32-bit floats: the header's lengths, and a round trip through the engine's own WAV reader.</summary>
public class WavWriterTests
{
	[Test]
	public void A_hundred_stereo_frames_make_a_44_byte_header_and_800_bytes_of_data()
	{
		using var stream = new MemoryStream();
		WavWriter.Write(stream, new float[200], 2, 48_000);

		var bytes = stream.ToArray();
		bytes.Length.ShouldBe(44 + 800);
		BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)).ShouldBe(836);
		BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)).ShouldBe(800);
	}

	[Test]
	public void Mono_samples_load_back_through_the_engine_exactly()
	{
		var samples = new float[1000];
		for (var i = 0; i < samples.Length; i++)
			samples[i] = MathF.Sin(i * 0.05f) * 0.7f;
		using var stream = new MemoryStream();
		WavWriter.Write(stream, samples, 1, 48_000);

		using var engine = new AudioEngine(new OfflineAudioDevice(48_000));
		var clip = engine.LoadWav(stream.ToArray());

		clip.Samples.ShouldBe(samples);
	}

	[Test]
	public void Three_channels_throw()
	{
		using var stream = new MemoryStream();
		Should.Throw<ArgumentOutOfRangeException>(() => WavWriter.Write(stream, new float[300], 3, 48_000));
	}
}
