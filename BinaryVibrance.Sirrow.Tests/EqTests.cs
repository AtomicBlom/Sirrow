using BinaryVibrance.Sirrow.Effects;

using static BinaryVibrance.Sirrow.Tests.EffectRig;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>The four-band EQ: a peak lifts its own frequency and leaves one far off alone.</summary>
public class EqTests
{
	private static float GainDb(double hz)
	{
		var eq = new Eq();
		eq.Peak1Hz.Value = 1000f;
		eq.Peak1Db.Value = 6f;
		var input = Sine(hz, 2 * Rate, 0.25f);
		var left = (float[])input.Clone();
		var right = (float[])input.Clone();

		Run(Prepared(eq), left, right);

		return AudioAnalysis.ToDb(AudioAnalysis.LevelAt(left.AsSpan(Rate / 2), hz, Rate))
			- AudioAnalysis.ToDb(AudioAnalysis.LevelAt(input.AsSpan(Rate / 2), hz, Rate));
	}

	[Test]
	public void A_peak_lifts_its_frequency_and_leaves_the_rest()
	{
		GainDb(1000).ShouldBe(6f, 0.5f);
		GainDb(100).ShouldBe(0f, 0.5f);
	}

	[Test]
	public void It_allocates_nothing_once_running()
	{
		var eq = Prepared(new Eq());
		eq.LowDb.Value = 3f;
		eq.HighDb.Value = -3f;
		AllocatedWhileRunning(eq).ShouldBe(0);
	}
}
