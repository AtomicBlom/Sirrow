using BinaryVibrance.Sirrow.Synth;
using BinaryVibrance.Sirrow.Synth.Effects;

using static BinaryVibrance.Sirrow.Tests.EffectRig;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>The delay: where its echoes land, how loud the second is, and which side a ping-pong's are in.</summary>
public class DelayTests
{
	private static Delay Echo(bool pingPong = false)
	{
		var delay = new Delay(pingPong);
		delay.TimeSeconds.Value = 0.1f;
		delay.Wet.Value = 1f;
		delay.Dry.Value = 0f;
		return Prepared(delay);
	}

	private static int PeakIndex(float[] samples, int from, int to)
	{
		var best = from;
		for (var i = from; i < to; i++)
			if (MathF.Abs(samples[i]) > MathF.Abs(samples[best]))
				best = i;
		return best;
	}

	[Test]
	public void The_first_echo_lands_at_the_delay_time()
	{
		var left = Impulse(Rate / 2);
		var right = Impulse(Rate / 2);
		Run(Echo(), left, right);

		var at = PeakIndex(left, 0, 7000);
		at.ShouldBeInRange(4799, 4801);
		left[at].ShouldBe(1f, 0.02f);
	}

	[Test]
	public void The_second_echo_is_quieter_by_the_feedback_and_damping()
	{
		var left = Impulse(Rate / 2);
		var right = Impulse(Rate / 2);
		Run(Echo(), left, right);

		// The feedback (0.35) goes through the damping's one-pole low-pass at 4 kHz, which turns a one-sample click into a
		// smear whose highest sample is 0.41 of it: 0.143, measured
		var at = PeakIndex(left, 9000, 10_200);
		at.ShouldBeInRange(9598, 9602);
		MathF.Abs(left[at]).ShouldBe(0.1427f, 0.0143f);
	}

	[Test]
	public void Ping_pong_bounces_the_echoes_between_the_sides()
	{
		var left = Impulse(Rate / 2);
		var right = Impulse(Rate / 2);
		Run(Echo(pingPong: true), left, right);

		AudioAnalysis.Peak(left.AsSpan(4700, 200)).ShouldBeGreaterThan(0.5f);
		AudioAnalysis.Peak(right.AsSpan(4700, 200)).ShouldBeLessThan(0.01f);
		AudioAnalysis.Peak(right.AsSpan(9500, 200)).ShouldBeGreaterThan(0.05f);
		AudioAnalysis.Peak(left.AsSpan(9500, 200)).ShouldBeLessThan(0.01f);
	}

	[Test]
	public void It_allocates_nothing_once_running() =>
		AllocatedWhileRunning(Prepared(new Delay())).ShouldBe(0);
}
