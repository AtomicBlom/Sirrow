using Loom.Audio.Synth;
using Loom.Audio.Synth.Effects;

using static Loom.Tests.EffectRig;

namespace Loom.Tests;

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

	[Test]
	public void A_short_tail_dies_away()
	{
		var reverb = new Reverb();
		reverb.DecaySeconds.Value = 0.5f;
		Prepared(reverb);
		Run(reverb, Noise(Rate, 0.5f, seed: 1), Noise(Rate, 0.5f, seed: 2));
		var silence = new float[5 * Rate];
		Run(reverb, silence, new float[silence.Length]);

		var left = new float[256];
		var right = new float[256];
		reverb.Process(left, right);

		MathF.Max(AudioAnalysis.Peak(left), AudioAnalysis.Peak(right)).ShouldBeLessThan(1e-6f);
	}

	[Test]
	public void It_allocates_nothing_once_running() =>
		AllocatedWhileRunning(Prepared(new Reverb())).ShouldBe(0);
}
