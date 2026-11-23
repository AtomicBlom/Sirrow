using BinaryVibrance.Sirrow;
using BinaryVibrance.Sirrow.Synth;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>
/// A source rendered ahead of the speaker: its frames heard in order with none lost, the lead kept, silence and a count
/// when the work falls behind, the end and a fault passed on, and nothing allocated once it plays.
/// </summary>
public class AheadSourceTests
{
	private const int Rate = 48_000;
	private const int Block = 256;

	// Writes its own frame number, divided by a million, to both sides, so order and gaps can be read off the output
	private sealed class CountingSource(long endAfter = long.MaxValue) : AudioSource
	{
		private long _frame;

		protected override void OnStart(int sampleRate) => _frame = 0;

		protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
		{
			for (var i = 0; i < info.Frames && _frame < endAfter; i++, _frame++)
			{
				left[i] = _frame / 1_000_000f;
				right[i] = _frame / 1_000_000f;
			}
			return _frame < endAfter;
		}
	}

	private sealed class ThrowingSource : AudioSource
	{
		protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info) =>
			throw new FormatException("synth bug");
	}

	[Test]
	public void Its_frames_are_heard_in_order_from_the_first_with_no_gap()
	{
		using var session = new OfflineSession(Rate);
		using var ahead = new AheadSource(session.Engine, new CountingSource());
		session.Engine.Play(ahead);

		session.Render(100 * Block);

		var left = session.Left();
		for (var i = 0; i < left.Length; i++)
			left[i].ShouldBe(i / 1_000_000f, $"frame {i}");
	}

	[Test]
	public void What_waits_stays_between_nothing_and_the_lead_and_a_block()
	{
		using var session = new OfflineSession(Rate);
		using var ahead = new AheadSource(session.Engine, new CountingSource());
		session.Engine.Play(ahead);
		var lead = (long)Math.Ceiling(ahead.LeadSeconds * Rate);

		for (var i = 0; i < 100; i++)
		{
			session.Render(Block);
			var waiting = ahead.FramesRendered - ahead.FramesHeard;
			waiting.ShouldBeInRange(0, lead + Block);
		}
	}

	[Test]
	public void Starved_it_plays_silence_and_counts_it_then_carries_on_with_none_lost()
	{
		// The device rendered directly, with no Update between blocks: nothing tops the ring up
		var device = new OfflineAudioDevice(Rate, Block);
		using var engine = new AudioEngine(device, new AudioOptions { Work = AudioWork.Update });
		using var ahead = new AheadSource(engine, new CountingSource());
		engine.Play(ahead);

		var starved = new float[50 * Block * 2];
		device.Render(starved);
		var heard = ahead.FramesHeard;
		heard.ShouldBeGreaterThan(0);
		heard.ShouldBeLessThan(50 * Block);
		for (var i = 0; i < heard; i++)
			starved[2 * i].ShouldBe(i / 1_000_000f);
		for (var i = (int)heard; i < 50 * Block; i++)
			starved[2 * i].ShouldBe(0f);
		ahead.Underruns.ShouldBeGreaterThan(0);

		// Updates again: the numbers carry on from where they stopped
		var block = new float[Block * 2];
		var next = heard;
		for (var b = 0; b < 20; b++)
		{
			engine.Update();
			device.Render(block);
			for (var i = 0; i < Block; i++, next++)
				block[2 * i].ShouldBe(next / 1_000_000f);
		}
	}

	[Test]
	public void A_source_that_ends_is_heard_to_its_last_frame_and_stops()
	{
		using var session = new OfflineSession(Rate);
		using var ahead = new AheadSource(session.Engine, new CountingSource(endAfter: 10_000));
		session.Engine.Play(ahead);

		session.Render(80 * Block);

		var left = session.Left();
		// Frame 0 is silent by its own number; count the frames from 1 that sound, and none after
		left.Skip(1).Count(s => s != 0f).ShouldBe(9_999);
		left[9_999].ShouldBe(9_999 / 1_000_000f);
		left[10_000].ShouldBe(0f);
		ahead.IsPlaying.ShouldBeFalse();
	}

	[Test]
	public void A_fault_rendering_it_is_thrown_by_a_later_update()
	{
		var device = new OfflineAudioDevice(Rate, Block);
		using var engine = new AudioEngine(device, new AudioOptions { Work = AudioWork.Update });
		using var ahead = new AheadSource(engine, new ThrowingSource());
		engine.Play(ahead);

		Should.Throw<InvalidOperationException>(engine.Update);
	}

	[Test]
	public void It_renders_and_fills_without_allocating_once_warm()
	{
		var device = new OfflineAudioDevice(Rate, Block);
		using var engine = new AudioEngine(device, new AudioOptions { Work = AudioWork.Update });
		using var ahead = new AheadSource(engine, new CountingSource());
		engine.Play(ahead);
		var block = new float[Block * 2];
		for (var i = 0; i < 64; i++)
		{
			device.Render(block);
			engine.Update();
		}

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 512; i++)
			{
				device.Render(block);
				engine.Update();
			}
		});
		block.Max().ShouldBeGreaterThan(0f);   // and frames came through: not silence allocating nothing
	}

	[Test]
	[NotInParallel]
	public void On_its_own_thread_it_keeps_up_with_a_device_in_real_time()
	{
		using var engine = new AudioEngine(new NullAudioDevice(Rate), new AudioOptions { Work = AudioWork.Thread });
		using var ahead = new AheadSource(engine, new CountingSource());
		engine.Play(ahead);

		Thread.Sleep(1000);
		engine.Update();

		ahead.Underruns.ShouldBe(0);
		// A second of wall clock, give or take what a loaded machine's sleep and scheduler add
		ahead.FramesHeard.ShouldBeInRange((long)(0.75 * Rate), (long)(1.25 * Rate));
	}
}
