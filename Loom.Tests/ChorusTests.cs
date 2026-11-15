using Loom.Audio.Synth;
using Loom.Audio.Synth.Effects;

using static Loom.Tests.EffectRig;

namespace Loom.Tests;

/// <summary>The chorus: silent at no mix, about as loud as its input, and wider.</summary>
public class ChorusTests
{
	[Test]
	public void At_no_mix_it_leaves_the_sound_alone()
	{
		var chorus = new Chorus();
		chorus.Mix.Value = 0f;
		var input = Sine(440, Rate, 0.5f);
		var left = (float[])input.Clone();
		var right = (float[])input.Clone();

		Run(Prepared(chorus), left, right);

		for (var i = 0; i < input.Length; i++)
		{
			left[i].ShouldBe(input[i]);
			right[i].ShouldBe(input[i]);
		}
	}

	[Test]
	public void It_keeps_the_level_and_widens_the_sound()
	{
		var input = Sine(440, 2 * Rate, 0.5f);
		var left = (float[])input.Clone();
		var right = (float[])input.Clone();

		Run(Prepared(new Chorus()), left, right);

		var settled = Rate / 2;
		var change = AudioAnalysis.ToDb(AudioAnalysis.Rms(left.AsSpan(settled))) - AudioAnalysis.ToDb(AudioAnalysis.Rms(input.AsSpan(settled)));
		MathF.Abs(change).ShouldBeLessThan(3f);
		var differs = false;
		for (var i = settled; i < left.Length && !differs; i++)
			differs = MathF.Abs(left[i] - right[i]) > 1e-3f;
		differs.ShouldBeTrue();
	}

	[Test]
	public void It_allocates_nothing_once_running() =>
		AllocatedWhileRunning(Prepared(new Chorus())).ShouldBe(0);
}
