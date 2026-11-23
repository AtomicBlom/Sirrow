using BinaryVibrance.Sirrow.Synth.Chip;
using BinaryVibrance.Sirrow.Synth.Music;

namespace BinaryVibrance.Sirrow.Tests;

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
	public void Notes_stacked_in_a_step_sound_together_and_share_their_holds()
	{
		var notes = Notes(Line.Quarters("1/3/5 - . b7'/2"));

		notes.Select(n => (n.Tick, n.Length, n.Degree)).ShouldBe([(0, 1920, 1), (0, 1920, 3), (0, 1920, 5), (2880, 960, 7), (2880, 960, 2)]);
		notes[3].Accidental.ShouldBe(-1);
		notes[3].Octave.ShouldBe(1);
		Should.Throw<NotationException>(() => Line.Quarters("1//3 . . .").Parse(Meter.FourFour));
		NoteToken.ParseStack("1/3/5")!.Select(n => n.Degree).ShouldBe([1, 3, 5]);
		NoteToken.ParseStack("1/x").ShouldBeNull();
		NoteToken.Stack([new NoteToken(5), new NoteToken(1), new NoteToken(3, Accidental: -1)]).ShouldBe("1/b3/5");
	}

	[Test]
	public void A_close_voiced_strum_plays_the_chord_as_a_line_stacked_would()
	{
		var song = new Song("keys");
		var keys = song.Track("keys", ChipRig.Tone);
		var a = song.Section("a", 1, "IV");
		a.Play(keys, Strum.Quarters("D...").Voicing(Voicing.Close).Spread(0f));
		a.Play(keys, Line.Quarters("4/6/8 - - -"));

		var compiled = CompiledSong.Compile(song, song.Scale).Sections[0];
		var strummed = compiled.Placements[0].Events.Select(e => ((int)e.Note, e.Tick)).Order().ToList();
		var written = compiled.Placements[1].Events.Select(e => ((int)e.Note, e.Tick)).Order().ToList();
		strummed.ShouldBe(written);
		strummed.Select(e => e.Item1).ShouldBe([65, 69, 72]);
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
