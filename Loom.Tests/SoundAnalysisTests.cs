using Loom.Audio.Synth.Analysis;

namespace Loom.Tests;

/// <summary>The analysis measures on signals whose answers are known: clicks, sines, a struck bar, a mix.</summary>
public class SoundAnalysisTests
{
	private const int Rate = 48_000;

	// A sine at hz dying 60 dB over decay seconds, from start for length seconds
	private static void Ring(float[] into, double start, double hz, double decay, float level = 0.5f)
	{
		var from = (int)(start * Rate);
		for (var i = from; i < into.Length; i++)
		{
			var t = (i - from) / (double)Rate;
			into[i] += (float)(level * Math.Exp(-6.9078 * t / decay) * Math.Sin(2 * Math.PI * hz * t));
		}
	}

	[Test]
	public void Onsets_are_found_where_each_hit_starts()
	{
		var signal = new float[Rate];
		Ring(signal, 0.2, 180, 0.1);
		Ring(signal, 0.6, 180, 0.1);

		var onsets = HitMeasures.Onsets(signal, Rate);
		onsets.Count.ShouldBe(2);
		onsets[0].ShouldBeInRange((int)(0.2 * Rate) - 10, (int)(0.2 * Rate) + 60);
		onsets[1].ShouldBeInRange((int)(0.6 * Rate) - 10, (int)(0.6 * Rate) + 60);
	}

	[Test]
	public void A_steady_low_tone_reads_its_pitch_and_a_bright_one_its_tones()
	{
		var low = new float[Rate / 2];
		Ring(low, 0, 110, 2.0);
		HitMeasures.Pitch(low, Rate, [20f, 80f])[0].ShouldBe(110f, 3f);

		var two = new float[Rate / 2];
		Ring(two, 0, 300, 2.0);
		Ring(two, 0, 1200, 2.0, 0.25f);
		var tones = HitMeasures.Tones(two, Rate, 4);
		tones.Count.ShouldBeGreaterThanOrEqualTo(2);
		tones[0].Hz.ShouldBe(300f, 3f);
		tones[1].Hz.ShouldBe(1200f, 6f);
		tones[1].Db.ShouldBe(-6f, 1f);
	}

	[Test]
	public void A_struck_bar_reads_as_inharmonic_partials_each_ringing_as_long_as_it_does()
	{
		// A free bar's first three modes, each dying faster than the one below it
		var bar = new float[3 * Rate];
		Ring(bar, 0, 200, 2.0);
		Ring(bar, 0, 200 * 2.76, 1.2, 0.3f);
		Ring(bar, 0, 200 * 5.40, 0.6, 0.15f);

		var partials = NoteMeasures.Partials(bar, Rate, out var f0);
		f0.ShouldBe(200f, 2f);
		partials.ShouldAllBe(p => p.Harmonic == 0);
		partials.ShouldContain(p => Math.Abs(p.Hz - 552f) < 4f);
		partials.ShouldContain(p => Math.Abs(p.Hz - 1080f) < 6f);
		NoteMeasures.Ring(bar, Rate, 200f).Early.ShouldBe(2.0f, 0.3f);
	}

	[Test]
	public void A_low_mono_tone_has_its_energy_in_its_band_and_both_sides_alike()
	{
		var stereo = new float[2 * 2 * Rate];
		for (var i = 0; i < stereo.Length / 2; i++)
			stereo[2 * i] = stereo[2 * i + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * 100 * i / Rate));

		var balance = MixMeasures.Balance(stereo, Rate, [60f, 125f, 250f])!;
		balance.Shares[1].ShouldBeGreaterThan(0.95);
		balance.CentreMedian.ShouldBe(100, 10);
		balance.Likeness.ShouldBe(1, 1e-6);
		balance.CrestDb.ShouldBe(3.01, 0.1);   // a sine's peak stands 3 dB over its RMS
		MixMeasures.Balance(new float[2 * Rate], Rate, [60f]).ShouldBeNull();
	}
}
