using BinaryVibrance.Sirrow.Synth;
using BinaryVibrance.Sirrow.Synth.Chip;
using BinaryVibrance.Sirrow.Synth.Dsp;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>The tube (the Synthesis Toolkit's flute): it speaks when blown, and in tune.</summary>
public class TubeOperatorTests
{
	private static Patch Flute => Patch.Build("flute", p =>
	{
		var breath = p.Level(Env.Adsr(0.04f, 0.1f, 0.8f, 0.1f));
		var noise = p.Noise(Env.Adsr(0.04f, 0.1f, 0.8f, 0.1f), 0.1f);
		p.Out(p.Tube().DrivenBy(breath, noise));
	});

	private static ChipRig Blow(float note, double seconds)
	{
		var rig = new ChipRig();
		rig.Chip.NoteOn(rig.Chip.AddInstrument(Flute), note, 1f);
		rig.Seconds(seconds);
		return rig;
	}

	[Test]
	public void It_speaks()
	{
		using var rig = Blow(72, 0.35);

		rig.RmsLeft(0.15, 0.15).ShouldBeGreaterThan(0.01f);
	}

	[Test]
	[Arguments(60f)]
	[Arguments(72f)]
	[Arguments(84f)]
	public void It_plays_in_tune(float note)
	{
		using var rig = Blow(note, 0.8);

		// Searched near the note, so a strong harmonic isn't read as the pitch
		var expected = Units.NoteToHz(note);
		var cents = 1200 * Math.Log2(rig.Pitch(0.3, 0.5, expected * 0.8, expected * 1.25) / expected);

		Math.Abs(cents).ShouldBeLessThan(20);
	}
}
