using BinaryVibrance.Sirrow.Effects;

using static BinaryVibrance.Sirrow.Tests.EffectRig;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>The distortion: harmonics made, the level kept under its output gain, aliasing held down by oversampling, and the dry path lined up.</summary>
public class DistortionTests
{
	[Test]
	public void Drive_makes_harmonics_under_the_output_level()
	{
		var distortion = new Distortion();
		distortion.DriveDb.Value = 30f;
		var left = Sine(220, 2 * Rate, 0.5f);
		var right = (float[])left.Clone();

		Run(Prepared(distortion), left, right);

		var settled = left.AsSpan(Rate);
		var fundamental = AudioAnalysis.ToDb(AudioAnalysis.LevelAt(settled, 220, Rate));
		var third = AudioAnalysis.ToDb(AudioAnalysis.LevelAt(settled, 660, Rate));
		(fundamental - third).ShouldBeLessThan(20f);
		AudioAnalysis.Peak(settled).ShouldBeLessThanOrEqualTo(AudioAnalysis.FromDb(-6f) * 1.05f);
	}

	// The energy below 20 kHz more than 30 Hz from every harmonic of a 1244.5 Hz sine driven hard: what aliasing adds
	private static double Stray(int oversampling)
	{
		const double hz = 1244.5;
		const int length = 65_536;
		var distortion = new Distortion(oversampling: oversampling);
		distortion.DriveDb.Value = 24f;
		distortion.ToneHz.Value = 20_000f;
		var left = Sine(hz, Rate + length, 0.5f);
		var right = (float[])left.Clone();

		Run(Prepared(distortion), left, right);

		var spectrum = AudioAnalysis.Spectrum(left.AsSpan(Rate, length));
		var binHz = (double)Rate / length;
		var stray = 0.0;
		for (var k = 1; k < spectrum.Length && k * binHz < 20_000; k++)
		{
			var f = k * binHz;
			var nearest = Math.Round(f / hz) * hz;
			if (Math.Abs(f - nearest) > 30)
				stray += spectrum[k] * (double)spectrum[k];
		}
		return stray;
	}

	[Test]
	public void Oversampling_holds_the_aliasing_down()
	{
		var plain = 10 * Math.Log10(Stray(1));
		var oversampled = 10 * Math.Log10(Stray(4));
		(plain - oversampled).ShouldBeGreaterThanOrEqualTo(20);
	}

	[Test]
	public void At_no_mix_it_is_the_dry_sound_delayed_to_line_up()
	{
		var distortion = new Distortion();
		distortion.Mix.Value = 0f;
		var input = Sine(440, Rate / 2, 0.5f);
		var left = (float[])input.Clone();
		var right = (float[])input.Clone();

		Run(Prepared(distortion), left, right);

		var delay = distortion.DryDelayFrames;
		delay.ShouldBe(47);
		for (var i = delay; i < input.Length; i++)
			left[i].ShouldBe(input[i - delay], 1e-4f);
	}

	[Test]
	public void It_allocates_nothing_once_running()
	{
		AllocatedWhileRunning(Prepared(new Distortion(DistortionCurve.Asymmetric, Cabinet.FourByTwelve))).ShouldBe(0);
		AllocatedWhileRunning(Prepared(new Distortion(DistortionCurve.Fold, oversampling: 1))).ShouldBe(0);
	}
}
