using Loom.Audio.Synth;
using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Dsp;

namespace Loom.Tests;

/// <summary>The plucked string: in tune across the range, and ringing for its decay time.</summary>
public class StringOperatorTests
{
	private static ChipRig Pluck(float note, float decaySeconds, double seconds)
	{
		var rig = new ChipRig();
		var pluck = rig.Chip.AddInstrument(Patch.Build("pluck", p => p.Out(p.String(1f, decaySeconds, damping: 0f))));
		rig.Chip.NoteOn(pluck, note, 1f);
		rig.Seconds(seconds);
		return rig;
	}

	[Test]
	[Arguments(45f)]
	[Arguments(57f)]
	[Arguments(69f)]
	[Arguments(81f)]
	public void It_plays_in_tune(float note)
	{
		using var rig = Pluck(note, 2f, 0.6);
		var expected = Units.NoteToHz(note);

		// Searched near the note: a string plucked by noise has every harmonic about as strong as the fundamental
		var ratio = rig.Pitch(0.05, 0.5, expected * 0.8, expected * 1.25) / expected;

		ratio.ShouldBeInRange(1 / 1.0029, 1.0029);
	}

	[Test]
	public void It_rings_for_its_decay_time()
	{
		using var rig = Pluck(57, 2f, 2.05);

		var fall = AudioAnalysis.ToDb(rig.RmsLeft(0.05, 0.1)) - AudioAnalysis.ToDb(rig.RmsLeft(1.9, 0.1));

		fall.ShouldBeInRange(45f, 75f);
	}
}
