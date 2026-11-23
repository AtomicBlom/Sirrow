using BinaryVibrance.Sirrow.Synth;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>The measures tests and reports take of rendered sound, against sines whose answers are known.</summary>
public class AudioAnalysisTests
{
	private static float[] Sine(float amplitude, double hz, int length, int rate = 48_000)
	{
		var samples = new float[length];
		for (var n = 0; n < length; n++)
			samples[n] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * n / rate));
		return samples;
	}

	[Test]
	public void A_sine_reads_its_rms_peak_and_level_and_nothing_elsewhere()
	{
		var sine = Sine(0.5f, 1000, 48_000);

		AudioAnalysis.Rms(sine).ShouldBe(0.35355f, 0.0005f);
		AudioAnalysis.Peak(sine).ShouldBe(0.5f, 0.001f);
		AudioAnalysis.LevelAt(sine, 1000, 48_000).ShouldBe(0.5f, 0.002f);
		AudioAnalysis.LevelAt(sine, 3000, 48_000).ShouldBeLessThan(0.0005f);
	}

	[Test]
	public void Decibels_go_both_ways()
	{
		AudioAnalysis.ToDb(0.5f).ShouldBe(-6.0206f, 0.001f);
		AudioAnalysis.FromDb(-6.0206f).ShouldBe(0.5f, 0.0001f);
	}

	[Test]
	public void A_spectrum_into_kept_buffers_is_the_same_spectrum_and_refuses_buffers_that_do_not_fit()
	{
		var sine = Sine(0.5f, 1000, 4096);
		var made = AudioAnalysis.Spectrum(sine);
		var (real, imaginary, bins) = (new double[4096], new double[4096], new float[2049]);

		AudioAnalysis.Spectrum(sine, real, imaginary, bins);
		AudioAnalysis.Spectrum(sine, real, imaginary, bins); // again, over what the first left in them
		bins.ShouldBe(made);

		Should.Throw<ArgumentException>(() => AudioAnalysis.Spectrum(sine, new double[3000], new double[3000], new float[1501]));
		Should.Throw<ArgumentException>(() => AudioAnalysis.Spectrum(sine, real, imaginary, new float[2048]));
		Should.Throw<ArgumentException>(() => AudioAnalysis.Spectrum(sine.AsSpan(0, 1000), real, imaginary, bins));
	}

	[Test]
	public void The_peak_frequency_finds_a_sine_between_bins()
	{
		var sine = Sine(0.5f, 440, 32_768);

		AudioAnalysis.PeakFrequency(sine, 48_000, 100, 2000).ShouldBe(440, 0.5);
	}
}
