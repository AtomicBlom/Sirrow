using BinaryVibrance.Sirrow.Synth;
using BinaryVibrance.Sirrow.Synth.Chip;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>A recording with a loop: it sustains past its own length, joins without a click, and still ends when let go.</summary>
public class SampleLoopTests
{
	private static float Db(float amplitude) => AudioAnalysis.ToDb(amplitude);

	// A patch that plays one recording, held for as long as the note is
	private static Patch Held(SampleData data, bool oneShot = false) =>
		Patch.Build("held", p => p.Out(p.Sample(new SampleSet(new SampleZone(data, RootNote: 69)), velocity: 0f, oneShot: oneShot)));

	private static ChipRig Played(Patch patch, float note, double held, double rendered)
	{
		var rig = new ChipRig(new ChipOptions { SpaceEffects = [], EchoEffects = [] });
		var instrument = rig.Chip.AddInstrument(patch);
		rig.Chip.Play(instrument, note, 1f, (float)held);
		rig.Seconds(rendered);
		return rig;
	}

	[Test]
	public void A_looped_recording_sounds_for_as_long_as_the_note_is_held()
	{
		// A quarter of a second of A 440, looped over whole periods of it, held for two seconds
		var looped = ChipRig.SineSample(440, 0.25).LoopedAt(440, 0.05, 0.15, 0);
		using var rig = Played(Held(looped), 69f, 2.0, 2.5);

		rig.RmsLeft(1.5, 0.2).ShouldBe(rig.RmsLeft(0.1, 0.1), 0.01f);
		rig.Pitch(1.5, 0.3).ShouldBe(440, 1.0);
		// And once let go, it ends
		rig.RmsLeft(2.3, 0.1).ShouldBeLessThan(1e-4f);

		// Without the loop the same recording is over in its quarter of a second
		using var once = Played(Held(ChipRig.SineSample(440, 0.25)), 69f, 2.0, 2.5);
		once.RmsLeft(1.5, 0.2).ShouldBeLessThan(1e-4f);
	}

	[Test]
	public void A_loop_of_whole_periods_joins_without_a_click()
	{
		var looped = ChipRig.SineSample(440, 0.25).LoopedAt(440, 0.05, 0.15, 0);
		using var rig = Played(Held(looped), 69f, 2.0, 2.0);

		// A click every time round would put energy everywhere: between the note's harmonics there is none to speak of
		var ring = rig.Left(0.5, 1.0);
		(Db(AudioAnalysis.LevelAt(ring, 440, ChipRig.Rate)) - Db(AudioAnalysis.LevelAt(ring, 660, ChipRig.Rate))).ShouldBeGreaterThan(70f);
	}

	[Test]
	public void A_crossfade_joins_a_loop_that_is_not_whole_periods()
	{
		// A loop 100.3 periods long: joined as it is, the wave jumps every time round; crossfaded, it never moves faster
		// than the note itself does (a sine of amplitude 0.5 at 440 Hz moves at most 0.029 a frame at 48 kHz)
		static float Steepest(int crossfade)
		{
			var looped = ChipRig.SineSample(440, 0.5).Looped(4800, 4800 + 10942, crossfade);
			using var rig = Played(Held(looped), 69f, 2.0, 2.0);
			var left = rig.Left(0.2, 1.5);
			var steepest = 0f;
			for (var i = 1; i < left.Length; i++)
				steepest = MathF.Max(steepest, MathF.Abs(left[i] - left[i - 1]));
			return steepest;
		}

		Steepest(0).ShouldBeGreaterThan(0.1f);
		Steepest(960).ShouldBeLessThan(0.035f);
	}

	[Test]
	public void A_loop_holds_its_pitch_played_an_octave_up_and_a_fifth_down()
	{
		var looped = ChipRig.SineSample(440, 0.25).LoopedAt(440, 0.05, 0.15, 0);
		using (var up = Played(Held(looped), 81f, 2.0, 2.0))
			up.Pitch(1.0, 0.5).ShouldBe(880, 2.0);
		using (var down = Played(Held(looped), 62f, 2.0, 2.0))
			down.Pitch(1.0, 0.5).ShouldBe(293.66, 1.0);
	}

	[Test]
	public void A_one_shot_plays_through_its_loop_to_its_end()
	{
		var looped = ChipRig.SineSample(440, 0.25).LoopedAt(440, 0.05, 0.15, 0);
		using var rig = Played(Held(looped, oneShot: true), 69f, 2.0, 1.0);

		rig.RmsLeft(0.1, 0.1).ShouldBeGreaterThan(0.2f);
		rig.RmsLeft(0.5, 0.2).ShouldBeLessThan(1e-4f);
	}

	[Test]
	public void A_loop_that_does_not_fit_is_refused()
	{
		var recording = ChipRig.SineSample(440, 0.1);
		var frames = recording.Samples.Length;
		Should.Throw<ArgumentOutOfRangeException>(() => recording.Looped(-1, 100));
		Should.Throw<ArgumentOutOfRangeException>(() => recording.Looped(100, 101));
		Should.Throw<ArgumentOutOfRangeException>(() => recording.Looped(100, frames + 1));
		Should.Throw<ArgumentOutOfRangeException>(() => recording.Looped(100, 2000, 200));    // longer than what comes before the loop
		Should.Throw<ArgumentOutOfRangeException>(() => recording.Looped(1000, 1100, 200));   // longer than the loop
		Should.Throw<ArgumentOutOfRangeException>(() => recording.LoopedAt(0, 0.01, 0.05));

		// The recording it was made from is left as it was
		var looped = recording.Looped(1000, 2000, 500);
		recording.IsLooped.ShouldBeFalse();
		looped.IsLooped.ShouldBeTrue();
		looped.Samples.ShouldNotBeSameAs(recording.Samples);
	}

	[Test]
	public void A_recording_cut_short_under_its_loop_is_refused_by_a_set()
	{
		var looped = ChipRig.SineSample(440, 0.5).Looped(1000, 9000);
		var cut = looped with { Samples = looped.Samples[..4000] };

		Should.Throw<ArgumentException>(() => new SampleSet(new SampleZone(cut))).Message.ShouldContain("loop");
	}

	[Test]
	public void An_empty_set_is_refused() => Should.Throw<ArgumentException>(() => new SampleSet());
}
