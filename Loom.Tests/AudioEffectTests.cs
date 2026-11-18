using Loom.Audio;

namespace Loom.Tests;

/// <summary>
/// Effects on buses and sends between them, rendered offline: the order within a bus, sends made in any order and the
/// loops
/// refused, an effect that throws, and the steady-state render that must still allocate nothing.
/// </summary>
public class AudioEffectTests
{
	private const int Rate = 48_000;
	private const int Block = 256;

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

	// A stereo clip at the centre plays at a gain of 1 in each side
	private static AudioClip Constant(AudioEngine engine, float value, int frames = 100_000)
	{
		var samples = new float[frames * 2];
		Array.Fill(samples, value);
		return engine.CreateClip(samples, 2, Rate);
	}

	// The second block, by which every one-block ramp has finished
	private static float[] Settled(OfflineAudioDevice device)
	{
		Render(device);
		return Render(device);
	}

	private static void ShouldAllBe(float[] output, float expected)
	{
		for (var i = 0; i < output.Length; i++)
			output[i].ShouldBe(expected, 1e-5f, $"sample {i}");
	}

	[Test]
	public void An_effect_changes_its_bus()
	{
		var (engine, device) = Make();
		var sfx = engine.CreateBus("sfx");
		engine.AddEffect(sfx, new Halve());
		engine.Play(Constant(engine, 0.5f), sfx);

		ShouldAllBe(Settled(device), 0.25f);
	}

	[Test]
	public void Effects_run_in_the_order_added()
	{
		var (engine, device) = Make();
		var first = engine.CreateBus("first");
		engine.AddEffect(first, new AddTenth());
		engine.AddEffect(first, new Halve());
		engine.Play(Constant(engine, 0.5f), first);
		ShouldAllBe(Settled(device), 0.3f);

		var (engine2, device2) = Make();
		var other = engine2.CreateBus("other");
		engine2.AddEffect(other, new Halve());
		engine2.AddEffect(other, new AddTenth());
		engine2.Play(Constant(engine2, 0.5f), other);
		ShouldAllBe(Settled(device2), 0.35f);
	}

	[Test]
	public void An_effect_runs_before_its_bus_volume()
	{
		var (engine, device) = Make();
		var sfx = engine.CreateBus("sfx");
		engine.AddEffect(sfx, new AddTenth());
		engine.SetBusVolume(sfx, 0.5f);
		engine.Play(Constant(engine, 0.5f), sfx);

		ShouldAllBe(Settled(device), 0.3f);
	}

	[Test]
	public void An_effect_on_the_master_changes_everything()
	{
		var (engine, device) = Make();
		var sfx = engine.CreateBus("sfx");
		engine.AddEffect(engine.Master, new Halve());
		engine.Play(Constant(engine, 0.25f), sfx);
		engine.Play(Constant(engine, 0.25f));

		ShouldAllBe(Settled(device), 0.25f);
	}

	[Test]
	public void A_bypassed_effect_lets_the_sound_past()
	{
		var (engine, device) = Make();
		var sfx = engine.CreateBus("sfx");
		engine.AddEffect(sfx, new Halve { Bypass = true });
		engine.Play(Constant(engine, 0.5f), sfx);

		ShouldAllBe(Settled(device), 0.5f);
	}

	[Test]
	public void A_send_mixes_a_bus_into_another_as_well_as_its_parent()
	{
		var (engine, device) = Make();
		var space = engine.CreateBus("space");
		var sfx = engine.CreateBus("sfx");
		engine.AddSend(sfx, space, 0.5f);
		engine.Play(Constant(engine, 0.5f), sfx);
		ShouldAllBe(Settled(device), 0.75f);

		engine.SetBusVolume(space, 0f);
		ShouldAllBe(Settled(device), 0.5f);
	}

	[Test]
	public void A_send_level_moves()
	{
		var (engine, device) = Make();
		var space = engine.CreateBus("space");
		var sfx = engine.CreateBus("sfx");
		var send = engine.AddSend(sfx, space, 0.5f);
		engine.Play(Constant(engine, 0.5f), sfx);
		Render(device);

		engine.SetSendLevel(send, 1f);
		engine.GetSendLevel(send).ShouldBe(1f);
		ShouldAllBe(Settled(device), 1f);
	}

	[Test]
	public void A_send_to_a_bus_made_after_it_mixes_as_one_made_before()
	{
		// The receiving bus made last: the engine finishes the sender first all the same
		var (engine, device) = Make();
		var sfx = engine.CreateBus("sfx");
		var space = engine.CreateBus("space");
		engine.AddSend(sfx, space, 0.5f);
		engine.Play(Constant(engine, 0.5f), sfx);
		ShouldAllBe(Settled(device), 0.75f);
	}

	[Test]
	public void A_chain_of_sends_made_in_any_order_mixes_whole()
	{
		// a sends a quarter on to b, b a quarter on to c, each made before the bus it sends to: 0.5 into a, 0.125 of it into
		// b, and a quarter of b's 0.125 into c
		var (engine, device) = Make();
		var a = engine.CreateBus("a");
		var b = engine.CreateBus("b");
		var c = engine.CreateBus("c");
		engine.AddSend(b, c, 0.25f);
		engine.AddSend(a, b, 0.25f);
		engine.Play(Constant(engine, 0.5f), a);
		ShouldAllBe(Settled(device), 0.5f + 0.125f + 0.125f * 0.25f);
	}

	[Test]
	public void A_send_that_would_make_a_loop_or_double_back_throws()
	{
		var (engine, _) = Make();
		var sfx = engine.CreateBus("sfx");
		var child = engine.CreateBus("child", sfx);
		var x = engine.CreateBus("x");
		var y = engine.CreateBus("y");
		engine.AddSend(x, y);

		Should.Throw<ArgumentException>(() => engine.AddSend(sfx, sfx));            // itself
		Should.Throw<ArgumentException>(() => engine.AddSend(child, sfx));          // into what it is already mixed into
		Should.Throw<ArgumentException>(() => engine.AddSend(sfx, child));          // a parent to its child: a loop
		Should.Throw<ArgumentException>(() => engine.AddSend(y, x)).Message.ShouldContain("loop");
		Should.Throw<ArgumentException>(() => engine.AddSend(engine.Master, sfx));  // the master, mixed last
	}

	[Test]
	public void One_effect_instance_belongs_to_one_bus()
	{
		var (engine, _) = Make();
		var a = engine.CreateBus("a");
		var b = engine.CreateBus("b");
		var halve = new Halve();
		engine.AddEffect(a, halve);

		Should.Throw<InvalidOperationException>(() => engine.AddEffect(b, halve));
	}

	[Test]
	public void An_effect_that_throws_is_stopped_and_thrown_on_update()
	{
		var (engine, device) = Make();
		var sfx = engine.CreateBus("sfx");
		engine.AddEffect(sfx, new Throws());
		engine.Play(Constant(engine, 0.5f), sfx);

		ShouldAllBe(Settled(device), 0.5f);
		Should.Throw<InvalidOperationException>(engine.Update);
		Should.NotThrow(engine.Update);
	}

	[Test]
	public void The_mix_with_effects_and_sends_allocates_nothing_once_running()
	{
		var (engine, device) = Make();
		var space = engine.CreateBus("space");
		var sfx = engine.CreateBus("sfx");
		var music = engine.CreateBus("music");
		engine.AddEffect(sfx, new Halve());
		var send = engine.AddSend(sfx, space, 0.3f);
		var duck = engine.AddDuck(sfx, music, depthDb: -9f, attackSeconds: 0.01f, releaseSeconds: 0.05f);
		var loop = engine.Play(Constant(engine, 0.2f, 777), music, pitch: 1.37f, loop: true);
		var shot = Constant(engine, 0.3f, 300);
		var output = new float[Block * 2];

		void Frame(int i)
		{
			engine.SetVolume(loop, 0.5f + (i % 2) * 0.25f, fadeSeconds: 0.01f);
			engine.SetBusVolume(sfx, i % 2 == 0 ? 1f : 0.8f);
			engine.SetSendLevel(send, 0.1f + (i % 3) * 0.2f, 0.02f);
			if (i % 16 == 0)
				engine.SetDuckEnabled(duck, i % 32 == 0);
			var hit = engine.Play(shot, sfx, pitch: 0.9f + (i % 5) * 0.05f, pan: -0.3f);
			device.Render(output);
			if (i % 4 == 0)
				engine.Stop(hit);
			engine.Update();
		}

		for (var i = 0; i < 64; i++)
			Frame(i);

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 512; i++)
				Frame(i);
		}, "Mixing through effects and sends allocated managed memory once running.");
	}

	private sealed class Halve : AudioEffect
	{
		public override void Process(Span<float> left, Span<float> right)
		{
			for (var i = 0; i < left.Length; i++)
			{
				left[i] *= 0.5f;
				right[i] *= 0.5f;
			}
		}
	}

	private sealed class AddTenth : AudioEffect
	{
		public override void Process(Span<float> left, Span<float> right)
		{
			for (var i = 0; i < left.Length; i++)
			{
				left[i] += 0.1f;
				right[i] += 0.1f;
			}
		}
	}

	private sealed class Throws : AudioEffect
	{
		public override void Process(Span<float> left, Span<float> right) => throw new InvalidOperationException("Thrown on purpose.");
	}
}
