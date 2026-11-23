using BinaryVibrance.Sirrow.Synth.Music;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>Chords laid across a guitar's strings, and a strum's strings in time.</summary>
public class StrumTests
{
	private static int[] Voice(int root, int[] intervals, Voicing voicing) => Strum.Voice(root, intervals, voicing);

	[Test]
	public void Open_chords_as_a_guitar_plays_them()
	{
		Voice(4, [0, 3, 7], Voicing.Open).ShouldBe([40, 47, 52, 55, 59, 64]);    // E minor
		Voice(9, [0, 3, 7], Voicing.Open).ShouldBe([45, 52, 57, 60, 64]);        // A minor
		Voice(0, [0, 4, 7], Voicing.Open).ShouldBe([48, 52, 55, 60, 64]);        // C major
		Voice(7, [0, 4, 7], Voicing.Open).ShouldBe([43, 47, 50, 55, 59, 67]);    // G major
	}

	[Test]
	public void Power_chords()
	{
		Voice(4, [0, 7], Voicing.Power).ShouldBe([40, 47, 52]);
		Voice(9, [0, 7], Voicing.Power).ShouldBe([45, 52, 57]);
	}

	[Test]
	public void A_down_then_an_up_stroke_in_time()
	{
		var song = new Song("strum") { Scale = Scale.Minor(Note.E), Tempo = 120 };
		var guitar = song.Track("guitar", ChipRig.Tone);
		song.Section("verse", 1, "i").Play(guitar, Strum.Quarters("D.U_"));

		var events = CompiledSong.Compile(song, song.Scale).Sections[0].Placements[0].Events.OrderBy(e => e.Tick).ToArray();

		var down = events.Where(e => e.Tick < 1920).ToArray();
		down.Length.ShouldBe(6);
		down.Select(e => e.Tick).ShouldBe([0, 23, 46, 69, 92, 115]);
		down.ShouldAllBe(e => e.Tick + e.Length == 1920);
		var up = events.Where(e => e.Tick >= 1920).ToArray();
		up.Length.ShouldBe(4);
		up[0].Note.ShouldBe(64f);
		up.Select(e => e.Note).ShouldBe([64f, 59f, 55f, 52f]);
		up.ShouldAllBe(e => e.Tick + e.Length == 2880);
	}

	[Test]
	public void A_bar_line_inside_a_bar_is_refused() =>
		Should.Throw<NotationException>(() => Strum.Eighths("D.DU .UD | U.DU .UDU").Parse(Meter.FourFour));
}
