using Loom.Audio.Synth.Music;

namespace Loom.Tests;

/// <summary>Scales by degree, into the octaves above and below, and pitches by name.</summary>
public class ScaleTests
{
	[Test]
	public void E_minor_by_degree()
	{
		var scale = Scale.Minor(Note.E);

		scale.Degree(1).Midi.ShouldBe(64);
		scale.Degree(5).Midi.ShouldBe(71);
		scale.Degree(8).Midi.ShouldBe(76);
		scale.Degree(0).Midi.ShouldBe(62);
		scale.Degree(3, accidental: -1).Midi.ShouldBe(66);
		scale.Degree(1, octave: 2).Midi.ShouldBe(40);
	}

	[Test]
	public void A_pentatonic_wraps_at_its_own_length() =>
		Scale.PentatonicMinor(Note.A).Degree(6).Midi.ShouldBe(81);

	[Test]
	public void A4_is_440() => Pitch.Of(Note.A, 4).Hz.ShouldBe(440f, 1e-3f);
}
