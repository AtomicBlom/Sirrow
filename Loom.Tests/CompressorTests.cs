using Loom.Audio.Synth;
using Loom.Audio.Synth.Effects;

using static Loom.Tests.EffectRig;

namespace Loom.Tests;

/// <summary>The compressor: a level over the threshold brought down by the ratio, one under it left alone.</summary>
public class CompressorTests
{
	private static float PeakDbAfterASecond(float amplitude)
	{
		var compressor = new Compressor();
		compressor.ThresholdDb.Value = -20f;
		compressor.Ratio.Value = 4f;
		compressor.KneeDb.Value = 0f;
		var left = Sine(1000, 2 * Rate, amplitude);
		var right = (float[])left.Clone();

		Run(Prepared(compressor), left, right);

		return AudioAnalysis.ToDb(AudioAnalysis.Peak(left.AsSpan(Rate)));
	}

	[Test]
	public void A_level_over_the_threshold_comes_down_by_the_ratio() =>
		PeakDbAfterASecond(1f).ShouldBe(-15f, 0.5f);

	[Test]
	public void A_level_under_the_threshold_passes() =>
		PeakDbAfterASecond(AudioAnalysis.FromDb(-30f)).ShouldBe(-30f, 0.1f);

	[Test]
	public void It_allocates_nothing_once_running() =>
		AllocatedWhileRunning(Prepared(new Compressor())).ShouldBe(0);
}
