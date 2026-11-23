using BinaryVibrance.Sirrow;
using BinaryVibrance.Sirrow.Synth;
using BinaryVibrance.Sirrow.Synth.Effects;

using static BinaryVibrance.Sirrow.Tests.EffectRig;

namespace BinaryVibrance.Sirrow.Tests;

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

	// A steady tone through a keyed compressor, keyed by a sine of keyAmplitude: the tone's peak over its second second, in decibels
	private static float KeyedPeakDb(float keyAmplitude)
	{
		var compressor = Prepared(new Compressor(keyed: true));
		compressor.ThresholdDb.Value = -20f;
		compressor.Ratio.Value = 4f;
		compressor.KneeDb.Value = 0f;
		var left = Sine(1000, 2 * Rate, 0.5f);
		var right = (float[])left.Clone();
		var key = Sine(500, 2 * Rate, keyAmplitude);
		for (var offset = 0; offset < left.Length; offset += AudioEffect.MaxBlockFrames)
		{
			var n = Math.Min(AudioEffect.MaxBlockFrames, left.Length - offset);
			compressor.Key(key.AsSpan(offset, n), key.AsSpan(offset, n));
			compressor.Process(left.AsSpan(offset, n), right.AsSpan(offset, n));
		}
		return AudioAnalysis.ToDb(AudioAnalysis.Peak(left.AsSpan(Rate)));
	}

	[Test]
	public void Keyed_it_turns_down_by_the_keys_level_not_its_own() =>
		// The key at 0 dB is 20 over the threshold, so 15 comes off: the tone, at -6, comes out at -21
		KeyedPeakDb(1f).ShouldBe(-6.02f - 15f, 0.5f);

	[Test]
	public void Keyed_by_silence_it_passes_however_loud_it_is() =>
		KeyedPeakDb(0f).ShouldBe(-6.02f, 0.05f);

	[Test]
	public void Keyed_with_no_key_it_throws_rather_than_doing_nothing()
	{
		var compressor = Prepared(new Compressor(keyed: true));
		var left = new float[64];
		var right = new float[64];
		Should.Throw<InvalidOperationException>(() => compressor.Process(left, right));
	}
}
