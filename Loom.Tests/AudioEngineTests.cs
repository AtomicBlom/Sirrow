using Loom.Audio;

namespace Loom.Tests;

/// <summary>
/// The mixer, rendered offline on the test's thread: voices, buses, sources, the handle and thread rules, and the
/// steady-state render that must not allocate (an allocation on the audio thread can stall it behind a collection).
/// </summary>
public class AudioEngineTests
{
	private const int Rate = 48_000;
	private const int Block = 256;
	private static readonly float CentreGain = MathF.Cos(MathF.PI / 4f); // equal-power pan at the centre: -3 dB a side

	private static (AudioEngine Engine, OfflineAudioDevice Device) Make(AudioOptions? options = null)
	{
		var device = new OfflineAudioDevice(Rate, Block);
		return (new AudioEngine(device, options), device);
	}

	private static float[] Render(OfflineAudioDevice device, int frames = Block)
	{
		var output = new float[frames * 2];
		device.Render(output);
		return output;
	}

	private static AudioClip Constant(AudioEngine engine, float value, int frames, int channels = 1)
	{
		var samples = new float[frames * channels];
		Array.Fill(samples, value);
		return engine.CreateClip(samples, channels, Rate);
	}

	[Test]
	public void A_centred_mono_clip_plays_at_minus_3_dB_in_each_side()
	{
		var (engine, device) = Make();
		engine.Play(Constant(engine, 0.5f, 1000));

		var output = Render(device);

		for (var i = 0; i < Block; i++)
		{
			output[i * 2].ShouldBe(0.5f * CentreGain, 1e-5f);
			output[i * 2 + 1].ShouldBe(0.5f * CentreGain, 1e-5f);
		}
	}

	[Test]
	public void A_clip_ends_at_its_last_frame_and_its_voice_is_freed_on_update()
	{
		var (engine, device) = Make();
		var voice = engine.Play(Constant(engine, 1f, 100));

		var output = Render(device);
		engine.Update();

		output[99 * 2].ShouldBeGreaterThan(0f);
		output[100 * 2].ShouldBe(0f);
		engine.IsPlaying(voice).ShouldBeFalse();
		engine.Stats.ActiveVoices.ShouldBe(0);
	}

	[Test]
	public void A_stale_handle_does_not_reach_the_voice_that_took_its_slot()
	{
		var (engine, device) = Make(new AudioOptions { MaxVoices = 1 });
		var first = engine.Play(Constant(engine, 1f, 10));
		Render(device);
		engine.Update();
		var second = engine.Play(Constant(engine, 1f, 10_000, channels: 2));

		second.Slot.ShouldBe(first.Slot);
		engine.SetVolume(first, 0f); // stale: must not silence the second voice
		var output = Render(device);

		output[(Block - 1) * 2].ShouldBe(1f, 1e-5f);
	}

	[Test]
	public void Stop_ramps_to_silence_across_one_block_and_ends_the_voice()
	{
		var (engine, device) = Make();
		var voice = engine.Play(Constant(engine, 1f, 64, channels: 2), loop: true);
		Render(device);

		engine.Stop(voice);
		var fading = Render(device);
		var after = Render(device);
		engine.Update();

		fading[0].ShouldBe(1f, 1e-5f);
		fading[(Block - 1) * 2].ShouldBeLessThan(0.01f);
		after.ShouldAllBe(s => s == 0f);
		engine.IsPlaying(voice).ShouldBeFalse();
	}

	[Test]
	public void A_bus_scales_what_is_mixed_into_it()
	{
		var (engine, device) = Make();
		var sfx = engine.CreateBus("sfx");
		engine.SetBusVolume(sfx, 0.5f);
		engine.Play(Constant(engine, 1f, 10_000, channels: 2), sfx);

		Render(device); // the change ramps across the first block
		var output = Render(device);

		output.ShouldAllBe(s => MathF.Abs(s - 0.5f) < 1e-5f);
		engine.GetBusName(sfx).ShouldBe("sfx");
		engine.GetBusVolume(sfx).ShouldBe(0.5f);
	}

	[Test]
	public void A_source_renders_until_it_says_it_has_finished()
	{
		var (engine, device) = Make();
		var source = new ConstantSource(0.25f, frames: 300);
		var voice = engine.Play(source);

		var output = Render(device, 512);
		engine.Update();

		source.StartedAt.ShouldBe(Rate);
		output[0].ShouldBe(0.25f, 1e-6f);
		output[255 * 2 + 1].ShouldBe(0.25f, 1e-6f);
		output[299 * 2].ShouldBe(0.25f, 1e-6f);
		output[300 * 2].ShouldBe(0f);
		engine.IsPlaying(voice).ShouldBeFalse();
		source.IsPlaying.ShouldBeFalse();
	}

	[Test]
	public void An_exception_in_a_source_stops_its_voice_and_is_thrown_from_update()
	{
		var (engine, device) = Make();
		engine.Play(new ThrowingSource());

		var output = Render(device);

		output.ShouldAllBe(s => s == 0f);
		var thrown = Should.Throw<InvalidOperationException>(engine.Update);
		thrown.InnerException.ShouldBeOfType<FormatException>();
		engine.Stats.ActiveVoices.ShouldBe(0);
	}

	[Test]
	public void A_pitch_of_2_plays_a_clip_twice_as_fast()
	{
		var (engine, device) = Make();
		var voice = engine.Play(Constant(engine, 1f, 2 * Block), pitch: 2f);

		Render(device);
		engine.Update();

		engine.IsPlaying(voice).ShouldBeFalse();
	}

	[Test]
	public void A_play_past_the_voice_limit_is_dropped_and_counted()
	{
		var (engine, _) = Make(new AudioOptions { MaxVoices = 2 });
		var clip = Constant(engine, 1f, 100);

		engine.Play(clip, loop: true).IsNone.ShouldBeFalse();
		engine.Play(clip, loop: true).IsNone.ShouldBeFalse();
		engine.Play(clip, loop: true).IsNone.ShouldBeTrue();

		engine.Stats.DroppedVoices.ShouldBe(1);
	}

	[Test]
	public void A_source_plays_on_one_voice_at_a_time()
	{
		var (engine, _) = Make();
		var source = new ConstantSource(0.1f, frames: int.MaxValue);
		engine.Play(source);

		Should.Throw<InvalidOperationException>(() => engine.Play(source));
	}

	[Test]
	public void A_stopped_source_plays_again_once_its_fade_is_rendered()
	{
		var (engine, device) = Make();
		var source = new ConstantSource(0.1f, frames: int.MaxValue);
		engine.Play(source);
		Render(device);

		engine.Stop(source);
		// The audio thread hasn't rendered the fade yet: the voice still plays it
		Should.Throw<InvalidOperationException>(() => engine.Play(source)).Message.ShouldContain("fade");

		Render(device);
		engine.Play(source).IsNone.ShouldBeFalse();
	}

	[Test]
	public void A_finished_source_plays_again_before_the_engine_updates()
	{
		var (engine, device) = Make();
		var source = new ConstantSource(0.1f, frames: Block / 2);
		engine.Play(source);
		Render(device);

		// No Update between: the play hears the voice end itself
		engine.Play(source).IsNone.ShouldBeFalse();
		source.IsPlaying.ShouldBeTrue();
	}

	[Test]
	public void The_engine_refuses_a_call_from_another_thread()
	{
		var (engine, _) = Make();
		var clip = Constant(engine, 1f, 100);
		Exception? thrown = null;

		// A thread of its own: a pool task could run on the very thread that made the engine
		var other = new Thread(() =>
		{
			try
			{
				engine.Play(clip);
			}
			catch (Exception ex)
			{
				thrown = ex;
			}
		});
		other.Start();
		other.Join();

		thrown.ShouldBeOfType<InvalidOperationException>().Message.ShouldContain("thread");
	}

	[Test]
	public void A_clip_is_converted_to_the_engine_rate_as_it_loads()
	{
		var (engine, _) = Make();
		var samples = new float[100];
		Array.Fill(samples, 0.5f);

		var clip = engine.CreateClip(samples, 1, Rate / 2);

		clip.SampleRate.ShouldBe(Rate);
		clip.Frames.ShouldBe(200);
	}

	[Test]
	public void The_mix_allocates_nothing_once_running()
	{
		var (engine, device) = Make();
		var sfx = engine.CreateBus("sfx");
		var music = engine.CreateBus("music");
		var loop = engine.Play(Constant(engine, 0.2f, 777, channels: 2), music, pitch: 1.37f, loop: true);
		var source = new ConstantSource(0.1f, frames: int.MaxValue);
		engine.Play(source, music);
		var shot = Constant(engine, 0.3f, 300);
		var output = new float[Block * 2];

		void Frame(int i)
		{
			engine.SetVolume(loop, 0.5f + (i % 2) * 0.25f, fadeSeconds: 0.01f);
			engine.SetPan(loop, (i % 3 - 1) * 0.5f);
			engine.SetBusVolume(sfx, i % 2 == 0 ? 1f : 0.8f);
			var hit = engine.Play(shot, sfx, pitch: 0.9f + (i % 5) * 0.05f, pan: -0.3f);
			device.Render(output);
			if (i % 4 == 0)
				engine.Stop(hit);
			engine.Update();
		}

		for (var i = 0; i < 64; i++)
			Frame(i); // warm up: every path compiled, every pool touched

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 512; i++)
			Frame(i);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		allocated.ShouldBe(0, "Playing, steering and mixing allocated managed memory once running.");
		engine.Stats.Callbacks.ShouldBe(576);
	}

	private sealed class ConstantSource(float value, int frames) : AudioSource
	{
		private int _left = frames;

		public int StartedAt { get; private set; }

		protected override void OnStart(int sampleRate) => StartedAt = sampleRate;

		protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
		{
			var n = Math.Min(_left, info.Frames);
			left[..n].Fill(value);
			right[..n].Fill(value);
			_left -= n;
			return _left > 0;
		}
	}

	private sealed class ThrowingSource : AudioSource
	{
		protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info) =>
			throw new FormatException("synth bug");
	}
}
