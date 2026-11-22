using Loom.Audio;
using Loom.Audio.Decoding.Flac;
using Loom.Audio.Decoding.Vorbis;

namespace Loom.Tests;

/// <summary>
/// Ogg Vorbis and FLAC through the engine: clips decoded whole, and streams decoded as they play. Rendered offline,
/// faster
/// than real time, so each test tops the stream's ring up itself before each block (the streaming thread does too,
/// under
/// the same lock), which keeps the output deterministic.
/// </summary>
public class AudioStreamTests
{
	private const int Block = 256;

	private static byte[] Ogg(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "AudioFixtures", name + ".ogg"));

	private static float[] Decoded(string name)
	{
		var decoder = new VorbisDecoder(new MemoryStream(Ogg(name)));
		var samples = new List<float>();
		var chunk = new float[1024 * decoder.Channels];
		int frames;
		while ((frames = decoder.Read(chunk)) > 0)
			samples.AddRange(chunk.AsSpan(0, frames * decoder.Channels));
		return [.. samples];
	}

	// Renders `frames` frames, topping the stream up before each block, until the voice ends or the frames run out
	private static float[] Play(AudioEngine engine, OfflineAudioDevice device, AudioStream stream, int frames)
	{
		var output = new float[frames * 2];
		for (var at = 0; at < frames; at += Block)
		{
			stream.Fill(int.MaxValue);
			device.Render(output.AsSpan(at * 2, Math.Min(Block, frames - at) * 2));
		}
		engine.Update();
		return output;
	}

	[Test]
	public void LoadOgg_makes_a_clip_of_the_whole_file()
	{
		using var engine = new AudioEngine(new OfflineAudioDevice(44_100, Block));

		var clip = engine.LoadOgg(Ogg("stereo-44k"));

		clip.Channels.ShouldBe(2);
		clip.Frames.ShouldBe(22_050);
		clip.Samples.ShouldBe(Decoded("stereo-44k"));
	}

	[Test]
	public void A_stream_at_the_engines_rate_plays_the_file_sample_for_sample_and_ends()
	{
		var device = new OfflineAudioDevice(44_100, Block);
		using var engine = new AudioEngine(device);
		using var stream = engine.OpenStream(Ogg("stereo-44k"));
		var expected = Decoded("stereo-44k");

		var voice = engine.Play(stream);
		var output = Play(engine, device, stream, 22_050 + 2 * Block);

		for (var i = 0; i < expected.Length; i++)
			output[i].ShouldBe(expected[i], 1e-6f);
		output.AsSpan(expected.Length).ToArray().ShouldAllBe(s => s == 0f);
		engine.IsPlaying(voice).ShouldBeFalse();
		stream.IsPlaying.ShouldBeFalse();
	}

	[Test]
	public void A_looping_stream_joins_its_end_to_its_start()
	{
		var device = new OfflineAudioDevice(44_100, Block);
		using var engine = new AudioEngine(device);
		using var stream = engine.OpenStream(Ogg("stereo-44k"), loop: true);
		var expected = Decoded("stereo-44k");

		engine.Play(stream);
		var output = Play(engine, device, stream, 22_050 * 2 + 1000);

		for (var i = 0; i < expected.Length; i++)
			output[expected.Length + i].ShouldBe(expected[i], 1e-6f);
		output[^1].ShouldBe(expected[1999], 1e-6f); // into the third time round
		stream.IsPlaying.ShouldBeTrue();
	}

	private static byte[] Flac(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "AudioFixtures", name + ".flac"));

	private static float[] DecodedFlac(string name)
	{
		var decoder = new FlacDecoder(new MemoryStream(Flac(name)));
		var samples = new float[decoder.TotalFrames * decoder.Channels];
		decoder.Read(samples).ShouldBe((int)decoder.TotalFrames);
		return samples;
	}

	[Test]
	public void LoadFlac_makes_a_clip_of_the_whole_file()
	{
		using var engine = new AudioEngine(new OfflineAudioDevice(44_100, Block));

		var clip = engine.LoadFlac(Flac("flac-stereo-44k"));

		clip.Channels.ShouldBe(2);
		clip.Frames.ShouldBe(11_025);
		clip.Samples.ShouldBe(DecodedFlac("flac-stereo-44k"));
	}

	[Test]
	public void A_FLAC_file_streams_and_loops_as_an_Ogg_one_does()
	{
		var device = new OfflineAudioDevice(44_100, Block);
		using var engine = new AudioEngine(device);
		using var stream = engine.OpenStream(Flac("flac-fixed-odd"), loop: true); // told from Ogg by its first bytes
		var expected = DecodedFlac("flac-fixed-odd");

		engine.Play(stream);
		var output = Play(engine, device, stream, 8_820 * 2 + 1000);

		for (var i = 0; i < expected.Length; i++)
		{
			output[i].ShouldBe(expected[i], 1e-6f);
			output[expected.Length + i].ShouldBe(expected[i], 1e-6f);
		}
		stream.IsPlaying.ShouldBeTrue();
	}

	[Test]
	public void FLAC_the_engine_cant_play_throws()
	{
		using var engine = new AudioEngine(new OfflineAudioDevice(48_000, Block));

		Should.Throw<NotSupportedException>(() => engine.LoadFlac(Flac("flac-surround")));
		Should.Throw<NotSupportedException>(() => engine.OpenStream(Flac("flac-surround")));
		Should.Throw<InvalidDataException>(() => engine.LoadFlac(Ogg("stereo-44k")));
	}

	[Test]
	public void A_mono_stream_plays_in_both_sides()
	{
		var device = new OfflineAudioDevice(48_000, Block);
		using var engine = new AudioEngine(device);
		using var stream = engine.OpenStream(Ogg("mono-48k-q0"));
		var expected = Decoded("mono-48k-q0");

		engine.Play(stream);
		var output = Play(engine, device, stream, 4096);

		for (var i = 0; i < 4096; i++)
		{
			output[i * 2].ShouldBe(expected[i], 1e-6f);
			output[i * 2 + 1].ShouldBe(expected[i], 1e-6f);
		}
	}

	[Test]
	public void A_stream_at_another_rate_is_converted_as_a_clip_of_it_would_be()
	{
		var device = new OfflineAudioDevice(48_000, Block);
		using var engine = new AudioEngine(device);
		using var stream = engine.OpenStream(Ogg("stereo-44k"));
		var clip = engine.LoadOgg(Ogg("stereo-44k"));

		engine.Play(stream);
		var output = Play(engine, device, stream, 26_000);

		var played = Array.FindLastIndex(output, s => s != 0f) / 2 + 1;
		played.ShouldBeInRange(23_990, 24_010); // 22,050 frames at 44.1 kHz last 24,000 at 48 kHz
		var maxError = 0f;
		for (var i = 100; i < 23_900; i++) // away from the ends, where each treats the edge its own way
		{
			maxError = MathF.Max(maxError, MathF.Abs(output[i * 2] - clip.Samples[i * 2]));
			maxError = MathF.Max(maxError, MathF.Abs(output[i * 2 + 1] - clip.Samples[i * 2 + 1]));
		}
		maxError.ShouldBeLessThan(1e-5f);
	}

	[Test]
	public void Each_play_of_a_stream_starts_from_the_beginning()
	{
		var device = new OfflineAudioDevice(44_100, Block);
		using var engine = new AudioEngine(device);
		using var stream = engine.OpenStream(Ogg("stereo-44k"));
		var expected = Decoded("stereo-44k");
		engine.Play(stream);
		Play(engine, device, stream, 8000);
		engine.Stop(stream);
		Play(engine, device, stream, Block * 2); // the fade, then the voice ends

		engine.Play(stream);
		var again = Play(engine, device, stream, 1000);

		for (var i = 0; i < 2000; i++)
			again[i].ShouldBe(expected[i], 1e-6f);
	}

	[Test]
	public void Disposing_a_playing_stream_stops_it()
	{
		var device = new OfflineAudioDevice(44_100, Block);
		using var engine = new AudioEngine(device);
		var stream = engine.OpenStream(Ogg("stereo-44k"), loop: true);
		var voice = engine.Play(stream);
		Play(engine, device, stream, 1000);

		stream.Dispose();
		Play(engine, device, stream, Block * 2);

		engine.IsPlaying(voice).ShouldBeFalse();
		Should.Throw<ObjectDisposedException>(() => engine.Play(stream));
	}

	[Test]
	public void A_stream_that_fails_to_decode_throws_from_Update()
	{
		var bytes = Ogg("stereo-44k");
		bytes[^200] ^= 0x10; // a damaged audio page, past the headers
		var device = new OfflineAudioDevice(44_100, Block);
		using var engine = new AudioEngine(device);
		using var stream = engine.OpenStream(bytes);
		engine.Play(stream);

		Should.Throw<InvalidOperationException>(() => Play(engine, device, stream, 22_050))
			.InnerException.ShouldBeOfType<InvalidDataException>();
	}

	[Test]
	public void A_stream_that_cannot_seek_is_read_into_memory_and_still_loops()
	{
		var device = new OfflineAudioDevice(44_100, Block);
		using var engine = new AudioEngine(device);
		using var stream = engine.OpenStream(new ForwardOnlyStream(Ogg("stereo-44k")), loop: true);

		engine.Play(stream);
		Play(engine, device, stream, 30_000);

		stream.IsPlaying.ShouldBeTrue();
	}

	[Test]
	public void Too_many_channels_and_bytes_that_are_not_Ogg_throw_at_open()
	{
		using var engine = new AudioEngine(new OfflineAudioDevice(48_000, Block));

		Should.Throw<NotSupportedException>(() => engine.OpenStream(Ogg("surround-48k")));
		Should.Throw<NotSupportedException>(() => engine.LoadOgg(Ogg("surround-48k")));
		Should.Throw<InvalidDataException>(() => engine.OpenStream(new byte[100]));
	}

	[Test]
	public void Playing_a_stream_allocates_nothing_once_running()
	{
		var device = new OfflineAudioDevice(44_100, Block);
		using var engine = new AudioEngine(device);
		using var stream = engine.OpenStream(Ogg("stereo-44k"), loop: true);
		engine.Play(stream);
		var output = new float[Block * 2];
		void Frame()
		{
			stream.Fill(int.MaxValue);
			device.Render(output);
			engine.Update();
		}
		for (var i = 0; i < 200; i++)
			Frame(); // through a loop's end and back to its start: every path taken

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 400; i++)
				Frame();
		});
	}

	private sealed class ForwardOnlyStream(byte[] bytes) : Stream
	{
		private readonly MemoryStream _inner = new(bytes);

		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
		public override void Flush() { }
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
