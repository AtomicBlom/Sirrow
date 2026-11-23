using BinaryVibrance.Sirrow.Effects;

using static BinaryVibrance.Sirrow.Tests.EffectRig;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>The feedback delay network reverb: how fast its tail falls, that it stays bounded, and that it is wide.</summary>
public class ReverbTests
{
	private static (float[] Left, float[] Right) ImpulseThrough(Reverb reverb, double seconds)
	{
		var length = (int)(seconds * Rate);
		var left = Impulse(length);
		var right = Impulse(length);
		Run(Prepared(reverb), left, right);
		return (left, right);
	}

	private static float RmsBetween(float[] samples, double from, double to) =>
		AudioAnalysis.Rms(samples.AsSpan((int)(from * Rate), (int)((to - from) * Rate)));

	[Test]
	public void Its_tail_falls_60_dB_in_the_decay_time()
	{
		var reverb = new Reverb();
		reverb.DecaySeconds.Value = 2f;
		reverb.DampingHz.Value = 20_000f;

		var (left, _) = ImpulseThrough(reverb, 2.3);

		var fall = AudioAnalysis.ToDb(RmsBetween(left, 0.1, 0.2)) - AudioAnalysis.ToDb(RmsBetween(left, 2.1, 2.2));
		fall.ShouldBeInRange(50f, 70f);
	}

	[Test]
	public void A_long_tail_fed_for_a_long_time_stays_bounded()
	{
		var reverb = Prepared(new Reverb());
		reverb.DecaySeconds.Value = 10f;
		var left = Noise(30 * Rate, 0.1f, seed: 1);
		var right = Noise(30 * Rate, 0.1f, seed: 2);

		Run(reverb, left, right);

		left.ShouldAllBe(x => float.IsFinite(x));
		right.ShouldAllBe(x => float.IsFinite(x));
		MathF.Max(AudioAnalysis.Peak(left), AudioAnalysis.Peak(right)).ShouldBeLessThan(4f);
		AudioAnalysis.Rms(left.AsSpan(20 * Rate)).ShouldBeGreaterThan(0.01f);   // a room still answering, not a silent one
	}

	[Test]
	public void Its_two_sides_differ()
	{
		var (left, right) = ImpulseThrough(new Reverb(), 1.0);

		var difference = 0.0;
		var level = 0.0;
		for (var i = 0; i < left.Length; i++)
		{
			difference += Math.Abs(left[i] - right[i]);
			level += Math.Abs(left[i]);
		}
		difference.ShouldBeGreaterThan(0.1 * level);
	}

	// The tail of a click, as blocks of 10 ms: how many of them, from 50 ms to 300 ms after it, are much quieter than
	// their neighbours. An echo that comes back as a few echoes leaves gaps; one that comes back as many fills them
	private static int Gaps(Reverb reverb)
	{
		const int Frames = EffectRig.Rate / 2;
		float[] left = EffectRig.Impulse(Frames), right = EffectRig.Impulse(Frames);
		EffectRig.Run(EffectRig.Prepared(reverb), left, right);
		const int Block = EffectRig.Rate / 100;
		var levels = new float[30];
		for (var b = 0; b < levels.Length; b++)
			levels[b] = AudioAnalysis.Rms(left.AsSpan((5 + b) * Block, Block));
		var gaps = 0;
		for (var b = 1; b < levels.Length - 1; b++)
			if (levels[b] < 0.35f * MathF.Min(levels[b - 1], levels[b + 1]))
				gaps++;
		return gaps;
	}

	[Test]
	public void A_click_s_tail_is_dense_from_the_start() => Gaps(new Reverb()).ShouldBe(0);

	[Test]
	public void Its_swaying_lines_keep_a_held_note_s_tail_from_ringing_on_a_few_frequencies()
	{
		// A second of one note, then its tail in a long room. Held still, the network rings on at its own frequencies
		// nearest the note, a few sharp lines a few hertz apart; swaying, those frequencies move and the lines blur into
		// each other. How far the strongest stands over the average of what is within 40 Hz of the note says which
		static float Sharpness(float modulation)
		{
			var reverb = EffectRig.Prepared(new Reverb { DecaySeconds = { Value = 6f }, Modulation = { Value = modulation } });
			const int Frames = 3 * EffectRig.Rate;
			var left = new float[Frames];
			var right = new float[Frames];
			EffectRig.Sine(1000, EffectRig.Rate, 0.5f).CopyTo(left, 0);
			EffectRig.Sine(1000, EffectRig.Rate, 0.5f).CopyTo(right, 0);
			EffectRig.Run(reverb, left, right);
			const int Size = 65536;
			var spectrum = AudioAnalysis.Spectrum(left.AsSpan(EffectRig.Rate + EffectRig.Rate / 3, Size));
			var bin = (double)EffectRig.Rate / Size;
			float strongest = 0f, sum = 0f;
			var count = 0;
			for (var k = (int)(960 / bin); k <= (int)(1040 / bin); k++)
			{
				strongest = MathF.Max(strongest, spectrum[k]);
				sum += spectrum[k];
				count++;
			}
			return strongest / (sum / count);
		}

		Sharpness(1f).ShouldBeLessThan(0.8f * Sharpness(0f));
	}

	[Test]
	public void A_short_tail_dies_away()
	{
		var reverb = new Reverb();
		reverb.DecaySeconds.Value = 0.5f;
		Prepared(reverb);
		Run(reverb, Noise(Rate, 0.5f, seed: 1), Noise(Rate, 0.5f, seed: 2));
		var silence = new float[5 * Rate];
		Run(reverb, silence, new float[silence.Length]);
		AudioAnalysis.Peak(silence.AsSpan(0, Rate / 10)).ShouldBeGreaterThan(1e-3f);   // there was a tail to die away

		var left = new float[256];
		var right = new float[256];
		reverb.Process(left, right);

		MathF.Max(AudioAnalysis.Peak(left), AudioAnalysis.Peak(right)).ShouldBeLessThan(1e-6f);
	}

	[Test]
	public void It_allocates_nothing_once_running() =>
		AllocatedWhileRunning(Prepared(new Reverb())).ShouldBe(0);

	[Test]
	public void A_size_outside_its_range_is_refused() => Should.Throw<ArgumentOutOfRangeException>(() => new Reverb(3f));
}
