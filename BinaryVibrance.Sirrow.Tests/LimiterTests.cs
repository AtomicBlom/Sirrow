using BinaryVibrance.Sirrow.Synth;
using BinaryVibrance.Sirrow.Synth.Effects;

using static BinaryVibrance.Sirrow.Tests.EffectRig;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>The limiter: nothing past its ceiling, and a quiet sound only delayed.</summary>
public class LimiterTests
{
	[Test]
	public void Nothing_gets_past_the_ceiling()
	{
		var left = Sine(440, Rate, 2f);
		var right = Sine(330, Rate, 2f);

		Run(Prepared(new Limiter()), left, right);

		var ceiling = AudioAnalysis.FromDb(-1f) + 1e-4f;
		AudioAnalysis.Peak(left).ShouldBeLessThanOrEqualTo(ceiling);
		AudioAnalysis.Peak(right).ShouldBeLessThanOrEqualTo(ceiling);
		// And held there, not merely silenced
		AudioAnalysis.Peak(left).ShouldBeGreaterThan(0.8f * ceiling);
	}

	[Test]
	public void A_quiet_sound_is_only_delayed()
	{
		var input = Sine(440, Rate, 0.25f);
		var left = (float[])input.Clone();
		var right = (float[])input.Clone();

		Run(Prepared(new Limiter()), left, right);

		for (var i = Limiter.LatencyFrames; i < input.Length; i++)
		{
			left[i].ShouldBe(input[i - Limiter.LatencyFrames], 1e-4f);
			right[i].ShouldBe(input[i - Limiter.LatencyFrames], 1e-4f);
		}
	}

	[Test]
	public void It_allocates_nothing_once_running() =>
		AllocatedWhileRunning(Prepared(new Limiter())).ShouldBe(0);
}
