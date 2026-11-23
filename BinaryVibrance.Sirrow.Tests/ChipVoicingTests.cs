using BinaryVibrance.Sirrow.Synth;
using BinaryVibrance.Sirrow.Synth.Chip;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>
/// What shapes a note beyond its operators' waves: a body's modes, a pluck and where it is made, how an operator follows
/// the keyboard, and a tremolo. Rendered offline and measured.
/// </summary>
public class ChipVoicingTests
{
	private static float Db(float amplitude) => AudioAnalysis.ToDb(amplitude);

	// A note of a patch held for a while, rendered from its start
	private static ChipRig Played(Patch patch, float note, double seconds, float velocity = 0.8f)
	{
		var rig = new ChipRig(new ChipOptions { SpaceEffects = [], EchoEffects = [] });
		var instrument = rig.Chip.AddInstrument(patch);
		rig.Chip.Play(instrument, note, velocity, (float)seconds);
		rig.Seconds(seconds);
		return rig;
	}

	[Test]
	public void Modes_ring_at_their_frequencies_for_their_times()
	{
		// A click into two modes: one at the note for a second, one at 2.76 times it for a tenth of that
		var bar = Patch.Build("bar", p =>
		{
			var click = p.Noise(Env.Decay(0.003f));
			p.Out(p.Modes(Mode.At(1f, 1f), Mode.At(2.76f, 0.1f)).DrivenBy(click));
		});
		using var rig = Played(bar, 69f, 1.0);

		var early = rig.Left(0.01, 0.08);
		Db(AudioAnalysis.LevelAt(early, 440 * 2.76, ChipRig.Rate)).ShouldBeGreaterThan(Db(AudioAnalysis.LevelAt(early, 440 * 1.9, ChipRig.Rate)) + 20f);
		Db(AudioAnalysis.LevelAt(early, 440, ChipRig.Rate)).ShouldBeGreaterThan(Db(AudioAnalysis.LevelAt(early, 440 * 1.9, ChipRig.Rate)) + 20f);

		// Half a second on, the long mode has fallen about 30 dB and the short one is gone
		var late = rig.Left(0.5, 0.2);
		var fall = Db(AudioAnalysis.LevelAt(rig.Left(0.05, 0.2), 440, ChipRig.Rate)) - Db(AudioAnalysis.LevelAt(late, 440, ChipRig.Rate));
		fall.ShouldBe(27f, 5f);
		Db(AudioAnalysis.LevelAt(late, 440 * 2.76, ChipRig.Rate)).ShouldBeLessThan(Db(AudioAnalysis.LevelAt(late, 440, ChipRig.Rate)) - 40f);
	}

	[Test]
	public void Modes_with_nothing_ringing_them_or_too_many_are_refused()
	{
		Should.Throw<PatchException>(() => Patch.Build("silent", p => p.Out(p.Modes(Mode.At(1f, 1f)))));
		Should.Throw<PatchException>(() => Patch.Build("crowded", p =>
		{
			var click = p.Noise(Env.Decay(0.003f));
			var modes = new Mode[Mode.Max + 1];
			Array.Fill(modes, Mode.At(1f, 1f));
			p.Out(p.Modes(modes).DrivenBy(click));
		}));
	}

	[Test]
	public void A_string_plucked_in_the_middle_has_no_even_harmonics()
	{
		float Second(float pick)
		{
			using var rig = Played(Patch.Build("string", p => p.Out(p.String(1f, 4f, 0.1f, pick))), 45f, 0.6);
			var ring = rig.Left(0.05, 0.5);
			return Db(AudioAnalysis.LevelAt(ring, 220, ChipRig.Rate)) - Db(AudioAnalysis.LevelAt(ring, 110, ChipRig.Rate));
		}

		// Plucked in the middle the second harmonic, which is still there, is far under the first; as it comes, it isn't
		Second(0.5f).ShouldBeLessThan(-25f);
		Second(0.2f).ShouldBeGreaterThan(-20f);
	}

	[Test]
	public void Every_pluck_of_a_high_note_is_as_loud_as_the_last()
	{
		using var rig = new ChipRig(new ChipOptions { SpaceEffects = [], EchoEffects = [] });
		var instrument = rig.Chip.AddInstrument(Patch.Build("string", p => p.Out(p.String(1f, 1.5f, 0.3f))));
		float loudest = float.MinValue, quietest = float.MaxValue;
		for (var i = 0; i < 12; i++)
		{
			rig.Session.Clear();
			rig.Chip.Play(instrument, 88f, 0.8f, 0.3f);
			rig.Seconds(0.5);
			var level = Db(rig.RmsLeft(0.0, 0.25));
			loudest = MathF.Max(loudest, level);
			quietest = MathF.Min(quietest, level);
			rig.Seconds(2.0);
		}

		(loudest - quietest).ShouldBeLessThan(3f);
	}

	[Test]
	public void An_operator_follows_the_keyboard_in_level_and_in_ring()
	{
		var patch = Patch.Build("keyed", p => p.Out(p.Sine(1f, Env.Decay(1f)).KeyLevel(-6f).KeyDecay(0.5f)));
		float Level(float note, double from)
		{
			using var rig = Played(patch, note, 1.0, velocity: 1f);
			return Db(rig.RmsLeft(from, 0.05));
		}

		// An octave up: 6 dB quieter at the start
		(Level(60f, 0.0) - Level(72f, 0.0)).ShouldBe(6f, 1.5f);
		// and ringing half as long: 60 dB a second at middle C is 15 dB in a quarter of a second, 30 dB an octave up
		(Level(60f, 0.0) - Level(60f, 0.25)).ShouldBe(15f, 2.5f);
		(Level(72f, 0.0) - Level(72f, 0.25)).ShouldBe(30f, 4f);
	}

	[Test]
	public void A_tremolo_dips_the_level_at_its_rate()
	{
		var patch = Patch.Build("swaying", p =>
		{
			p.Out(p.Sine(1f, Env.Gate));
			p.Tremolo(5f, 0.5f);
		});
		using var rig = Played(patch, 69f, 1.0, velocity: 1f);

		// Five dips a second: the loudest and quietest fiftieths of a second differ by the depth, a half, 6 dB
		float loudest = float.MinValue, quietest = float.MaxValue;
		for (var t = 0.2; t < 0.8; t += 0.02)
		{
			var level = Db(rig.RmsLeft(t, 0.02));
			loudest = MathF.Max(loudest, level);
			quietest = MathF.Min(quietest, level);
		}
		(loudest - quietest).ShouldBe(6f, 1.5f);
	}

	// Two notes a fifth apart, the lower heard at one pan and the upper at another; quiet enough that both in the middle
	// together stay under full scale
	private static Patch Pair(float lowPan, float highPan, FilterSpec? filter = null) => Patch.Build("pair", p =>
	{
		p.Out(p.Sine(1f, Env.Gate).At(0.4f, lowPan), p.Sine(1.5f, Env.Gate).At(0.4f, highPan));
		if (filter is not null)
			p.Filter(filter);
	});

	[Test]
	public void Operators_are_heard_where_their_pans_put_them()
	{
		using var rig = Played(Pair(-1f, 1f), 69f, 1.0, velocity: 1f);
		float[] left = rig.Left(0.2, 0.6), right = rig.Right(0.2, 0.6);

		// The lower note is all on the left and the upper all on the right
		(Db(AudioAnalysis.LevelAt(left, 440, ChipRig.Rate)) - Db(AudioAnalysis.LevelAt(left, 660, ChipRig.Rate))).ShouldBeGreaterThan(60f);
		(Db(AudioAnalysis.LevelAt(right, 660, ChipRig.Rate)) - Db(AudioAnalysis.LevelAt(right, 440, ChipRig.Rate))).ShouldBeGreaterThan(60f);
		// And each as loud there as the other is on its side
		Db(AudioAnalysis.LevelAt(left, 440, ChipRig.Rate)).ShouldBe(Db(AudioAnalysis.LevelAt(right, 660, ChipRig.Rate)), 0.1f);
	}

	[Test]
	public void An_operator_in_the_middle_of_a_wide_patch_is_as_loud_as_in_a_patch_that_is_not()
	{
		using var plain = Played(Pair(0f, 0f), 69f, 1.0, velocity: 1f);
		using var wide = Played(Pair(0f, 1f), 69f, 1.0, velocity: 1f);

		// The lower note sits in the middle of both: the same level on the left, and in the wide one on the right too
		var middle = Db(AudioAnalysis.LevelAt(plain.Left(0.2, 0.6), 440, ChipRig.Rate));
		Db(AudioAnalysis.LevelAt(wide.Left(0.2, 0.6), 440, ChipRig.Rate)).ShouldBe(middle, 0.05f);
		Db(AudioAnalysis.LevelAt(wide.Right(0.2, 0.6), 440, ChipRig.Rate)).ShouldBe(middle, 0.05f);
		// The upper, moved to the right, is 3 dB up there (all of it, where the middle gives each side half its power)
		var upper = Db(AudioAnalysis.LevelAt(plain.Right(0.2, 0.6), 660, ChipRig.Rate));
		Db(AudioAnalysis.LevelAt(wide.Right(0.2, 0.6), 660, ChipRig.Rate)).ShouldBe(upper + 3f, 0.1f);
	}

	[Test]
	public void A_wide_patch_s_filter_works_on_both_sides()
	{
		// Both notes well above a low-pass at 200 Hz: each side falls by the same amount as its note would in the middle
		var filter = Filter.LowPass(200f);
		using var open = Played(Pair(-1f, 1f), 69f, 1.0, velocity: 1f);
		using var shut = Played(Pair(-1f, 1f, filter), 69f, 1.0, velocity: 1f);
		using var middle = Played(Pair(0f, 0f, filter), 69f, 1.0, velocity: 1f);
		using var middleOpen = Played(Pair(0f, 0f), 69f, 1.0, velocity: 1f);

		float Fall(ChipRig with, ChipRig without, bool onRight, double hz) =>
			Db(AudioAnalysis.LevelAt(onRight ? without.Right(0.2, 0.6) : without.Left(0.2, 0.6), hz, ChipRig.Rate))
			- Db(AudioAnalysis.LevelAt(onRight ? with.Right(0.2, 0.6) : with.Left(0.2, 0.6), hz, ChipRig.Rate));

		Fall(shut, open, onRight: false, 440).ShouldBe(Fall(middle, middleOpen, onRight: false, 440), 0.2f);
		Fall(shut, open, onRight: true, 660).ShouldBe(Fall(middle, middleOpen, onRight: true, 660), 0.2f);
		Fall(shut, open, onRight: true, 660).ShouldBeGreaterThan(15f);
	}

	[Test]
	public void A_note_s_pan_leans_a_wide_patch_to_its_side()
	{
		using var rig = new ChipRig(new ChipOptions { SpaceEffects = [], EchoEffects = [] });
		var instrument = rig.Chip.AddInstrument(Pair(-1f, 1f));
		rig.Chip.Play(instrument, 69f, 1f, 1f, pan: -1f);
		rig.Seconds(1.0);

		// Played all the way left, its left side is as it was and its right is gone
		AudioAnalysis.LevelAt(rig.Left(0.2, 0.6), 440, ChipRig.Rate).ShouldBeGreaterThan(0.3f);
		AudioAnalysis.Peak(rig.Right(0.2, 0.6)).ShouldBeLessThan(1e-4f);
	}

	[Test]
	public void A_pan_past_either_side_is_refused()
	{
		Should.Throw<PatchException>(() => Patch.Build("off-stage", p => p.Out(p.Sine(1f, Env.Gate).At(1f, 1.5f))));
		Patch.Build("plain", p => p.Out(p.Sine(1f, Env.Gate))).Wide.ShouldBeFalse();
		Patch.Build("wide", p => p.Out(p.Sine(1f, Env.Gate).At(1f, 0.3f))).Wide.ShouldBeTrue();
	}

	[Test]
	public void A_falling_pitch_moves_within_a_block()
	{
		// A sine that starts two octaves up and falls in 20 ms: in the first block (256 frames, 5.3 ms) its first and last
		// quarters are at different pitches, where a pitch worked out once a block would hold one
		var drum = Patch.Build("drum", p =>
		{
			p.Out(p.Sine(1f, Env.Gate));
			p.PitchDrop(24f, 0.02f);
		});
		using var rig = Played(drum, 69f, 0.1, velocity: 1f);

		var left = rig.Session.Left();
		int Crossings(int from, int to)
		{
			var count = 0;
			for (var i = from + 1; i < to; i++)
				if (left[i - 1] < 0f != left[i] < 0f)
					count++;
			return count;
		}
		Crossings(0, 128).ShouldBeGreaterThan(Crossings(128, 256) + 2);
	}
}
