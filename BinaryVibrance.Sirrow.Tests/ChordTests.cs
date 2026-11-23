using BinaryVibrance.Sirrow.Synth.Music;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>Chords by Roman numeral: the scale's own triads, sevenths, flats, and the kinds that override the scale.</summary>
public class ChordTests
{
	private static readonly Scale CMajor = Scale.Major(Note.C);

	[Test]
	public void The_scale_decides_each_triad()
	{
		Chord.Parse("I").Intervals(CMajor).ShouldBe([0, 4, 7]);
		Chord.Parse("ii").Intervals(CMajor).ShouldBe([0, 3, 7]);
		Chord.Parse("vii").Intervals(CMajor).ShouldBe([0, 3, 6]);
		Chord.Parse("V7").Intervals(CMajor).ShouldBe([0, 4, 7, 10]);
		Should.Throw<NotationException>(() => Chord.Parse("Imaj"));
	}

	[Test]
	public void A_flat_moves_the_root()
	{
		var chord = Chord.Parse("bVII");
		chord.RootClass(CMajor).ShouldBe(10);
		chord.Intervals(CMajor).ShouldBe([0, 4, 7]);
	}

	[Test]
	public void In_E_minor()
	{
		var minor = Scale.Minor(Note.E);
		var power = Chord.Parse("i5");
		power.Intervals(minor).ShouldBe([0, 7]);
		power.RootClass(minor).ShouldBe(4);
		var six = Chord.Parse("VI");
		six.RootClass(minor).ShouldBe(0);
		six.Intervals(minor).ShouldBe([0, 4, 7]);
	}
}
