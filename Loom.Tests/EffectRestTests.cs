using Loom.Audio;
using Loom.Audio.Synth.Effects;

namespace Loom.Tests;

/// <summary>
/// An effect resting: <see cref="AudioEffect.Run"/> stops processing once silence has gone in and come out for the
/// effect's memory, and starts again with the first sound, without cutting a tail or an echo short.
/// </summary>
public class EffectRestTests
{
	private const int Block = AudioEffect.MaxBlockFrames;

	// Passes the sound through and counts the blocks it was asked to process
	private sealed class Counting : AudioEffect
	{
		public int Blocks;

		public override void Process(Span<float> left, Span<float> right) => Blocks++;
	}

	// Runs both channels through the effect as its owner would, a block at a time
	private static void Run(AudioEffect effect, float[] left, float[] right)
	{
		for (var offset = 0; offset < left.Length; offset += Block)
		{
			var n = Math.Min(Block, left.Length - offset);
			effect.Run(left.AsSpan(offset, n), right.AsSpan(offset, n));
		}
	}

	[Test]
	public void An_effect_rests_after_a_second_of_silence_and_wakes_with_sound()
	{
		var effect = EffectRig.Prepared(new Counting());
		var silence = new float[Block];

		// A second of silence in and out is 188 blocks; it processes those, and no more
		for (var i = 0; i < 400; i++)
			effect.Run(silence, silence);
		effect.Blocks.ShouldBe(188);
		effect.IsResting.ShouldBeTrue();

		var sound = EffectRig.Sine(440, Block, 0.5f);
		effect.Run(sound, sound);
		effect.Blocks.ShouldBe(189);
		effect.IsResting.ShouldBeFalse();
	}

	[Test]
	public void A_reverb_s_tail_is_heard_out_before_it_rests()
	{
		const int Frames = 8 * EffectRig.Rate;
		float[] directLeft = EffectRig.Impulse(Frames), directRight = EffectRig.Impulse(Frames);
		EffectRig.Run(EffectRig.Prepared(new Reverb()), directLeft, directRight);

		var reverb = EffectRig.Prepared(new Reverb());
		float[] left = EffectRig.Impulse(Frames), right = EffectRig.Impulse(Frames);
		Run(reverb, left, right);

		// The same tail, sample for sample, down to where what is left is under the silence it rests at
		reverb.IsResting.ShouldBeTrue();
		for (var i = 0; i < Frames; i++)
			left[i].ShouldBe(directLeft[i], 2e-6f, $"sample {i}");
	}

	[Test]
	public void An_echo_further_off_than_a_second_is_not_cut()
	{
		var delay = new Delay();
		delay.TimeSeconds.Value = 1.5f;
		delay.Feedback.Value = 0f;
		delay.Wet.Value = 1f;
		delay.Dry.Value = 0f;
		EffectRig.Prepared(delay);

		const int Frames = 4 * EffectRig.Rate;
		float[] left = EffectRig.Impulse(Frames), right = EffectRig.Impulse(Frames);
		Run(delay, left, right);

		var echo = 0f;
		for (var i = (int)(1.45 * EffectRig.Rate); i < (int)(1.55 * EffectRig.Rate); i++)
			echo = MathF.Max(echo, MathF.Abs(left[i]));
		echo.ShouldBeGreaterThan(0.5f);
		delay.IsResting.ShouldBeTrue();
	}

	[Test]
	public void A_rested_reverb_answers_as_a_fresh_one_does()
	{
		// With its lines held still: a swaying line is somewhere else in its sway by the time the rested one is asked again,
		// which is a different tail, and not what this is about
		static Reverb Still() => EffectRig.Prepared(new Reverb { Modulation = { Value = 0f } });

		var rested = Still();
		const int Wait = 8 * EffectRig.Rate;
		float[] waitLeft = EffectRig.Impulse(Wait), waitRight = EffectRig.Impulse(Wait);
		Run(rested, waitLeft, waitRight);
		rested.IsResting.ShouldBeTrue();

		const int Frames = EffectRig.Rate;
		float[] left = EffectRig.Impulse(Frames), right = EffectRig.Impulse(Frames);
		Run(rested, left, right);
		float[] freshLeft = EffectRig.Impulse(Frames), freshRight = EffectRig.Impulse(Frames);
		Run(Still(), freshLeft, freshRight);

		for (var i = 0; i < Frames; i++)
			left[i].ShouldBe(freshLeft[i], 1e-4f, $"sample {i}");
	}
}
