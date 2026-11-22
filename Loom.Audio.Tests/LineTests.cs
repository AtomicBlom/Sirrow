using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Music;

namespace Loom.Tests;

/// <summary>The line notation: steps, holds, marks, bars and its mistakes, and a line over the chords compiled.</summary>
public class LineTests
{
	private static Line.RawNote[] Notes(Line line) => ((Line.Parsed)line.Parse(Meter.FourFour)).Notes;

	[Test]
	public void Steps_holds_and_rests()
	{
		var notes = Notes(Line.Eighths("1 3 5 - 4 3 2 ."));

		notes.Select(n => n.Tick).ShouldBe([0, 480, 960, 1920, 2400, 2880]);
		notes[2].Length.ShouldBe(960);
		notes.Select(n => n.Degree).ShouldBe([1, 3, 5, 4, 3, 2]);
	}

	[Test]
	public void Octaves_accidentals_accents_and_slides()
	{
		var notes = Notes(Line.Eighths("1' 1, b3 #4 ^5 6~ . ."));

		notes.Select(n => n.Octave).ShouldBe([1, -1, 0, 0, 0, 0]);
		notes.Select(n => n.Accidental).ShouldBe([0, 0, -1, 1, 0, 0]);
		notes.Select(n => n.Velocity).ShouldBe([0.8f, 0.8f, 0.8f, 0.8f, 1f, 0.8f]);
		notes.Select(n => n.Slide).ShouldBe([false, false, false, false, false, true]);
	}

	[Test]
	public void A_short_bar_is_refused_naming_it()
	{
		var thrown = Should.Throw<NotationException>(() => Line.Eighths("1 2 3 4 5 6 7").Parse(Meter.FourFour));
		thrown.Message.ShouldContain("bar 1 has 7 steps");
		thrown.Message.ShouldContain("has 8");
	}

	[Test]
	public void A_hold_with_nothing_sounding_or_a_bad_token_is_refused()
	{
		Should.Throw<NotationException>(() => Line.Quarters("- 1 1 1").Parse(Meter.FourFour));
		var thrown = Should.Throw<NotationException>(() => Line.Quarters("1 h 1 1").Parse(Meter.FourFour));
		thrown.Column.ShouldBe(3);
		thrown.Message.ShouldContain("column 3");
	}

	[Test]
	public void Over_the_chords_degree_1_is_each_bars_root()
	{
		var song = new Song("over chords") { Scale = Scale.Minor(Note.E) };
		var bass = song.Track("bass", ChipRig.Tone).Octave(2);
		var verse = song.Section("verse", 2, "i | VI");
		verse.Play(bass, Line.Quarters("1 1 5 1").OverChords());

		var events = CompiledSong.Compile(song, song.Scale).Sections[0].Placements[0].Events;

		events.Select(e => (int)e.Note).ShouldBe([40, 40, 47, 40, 48, 48, 55, 48]);
	}
}
