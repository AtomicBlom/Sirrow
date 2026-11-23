using BinaryVibrance.Sirrow.Synth.Music;
using Shouldly;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>
/// A section's exits and its passes as the music model has them: <c>pass</c> in a condition, read and written; exits in a
/// <c>sirrow-song/2</c> file, read and written, and a <c>kaldra-song/1</c> file refused with how to move it; a part on one pass
/// alone; where a placement is in its section, what it plays through it, and the notes a track sounds; a section's chords
/// bar by bar.
/// </summary>
public class SectionExitTests
{
	private static readonly Cue Energy = new("energy");

	private static SongBindings Bindings => new()
	{
		Patch = n => n == MusicRig.Click.Name ? MusicRig.Click : null,
		Cue = n => n == "energy" ? Energy : null,
	};

	private const string File = """
		{
		  "format": "sirrow-song/2",
		  "name": "exits",
		  "tempo": 120,
		  "scale": "C major",
		  "tracks": [{ "name": "click", "patch": "click" }],
		  "sections": [
		    {
		      "name": "verse", "bars": 1, "chords": "I",
		      "parts": [{ "track": "click", "line": "quarters", "notes": "1 1 1 1" }],
		      "exits": [{ "when": "pass < 2", "then": "verse" }, { "when": "energy > 0.6", "then": "chorus" }, { "then": "verse" }]
		    },
		    {
		      "name": "chorus", "bars": 1, "chords": "IV",
		      "parts": [{ "track": "click", "line": "quarters", "notes": "1 1 1 1" }],
		      "exits": [{ "then": "verse" }]
		    }
		  ]
		}
		""";

	[Test]
	[Arguments("pass < 2")]
	[Arguments("pass > 1")]
	[Arguments("pass is 4")]
	[Arguments("pass > 1 and energy > 0.5")]
	public void Pass_is_read_and_written_as_a_condition(string text)
	{
		var condition = ConditionText.Parse(text, n => n == "energy" ? Energy : null);
		ConditionText.Write(condition).ShouldBe(text);
	}

	[Test]
	public void A_pass_condition_compares_exactly()
	{
		Pass.Below(2).IsMet(_ => 0f, 1).ShouldBeTrue();
		Pass.Below(2).IsMet(_ => 0f, 2).ShouldBeFalse();
		Pass.Above(1).IsMet(_ => 0f, 2).ShouldBeTrue();
		Pass.Is(3).IsMet(_ => 0f, 3).ShouldBeTrue();
		Energy.Above(0.5f).IsMet(_ => 0.5f).ShouldBeTrue();   // at the level, as the sequencer has it
	}

	[Test]
	public void A_pass_count_below_one_is_refused()
	{
		// Passes count from 1: pass is 0 or pass < 1 never holds, and pass > 0 always does
		Should.Throw<ArgumentOutOfRangeException>(() => Pass.Is(0));
		Should.Throw<ArgumentOutOfRangeException>(() => Pass.Below(0));
		Should.Throw<ArgumentOutOfRangeException>(() => Pass.Above(0));
		Should.Throw<FormatException>(() => ConditionText.Parse("pass is 0", _ => null)).Message.ShouldContain("count from 1");
		ConditionText.Parse("pass is 1", _ => null).IsMet(_ => 0f, 1).ShouldBeTrue();
	}

	[Test]
	public void Exits_are_read_from_and_written_to_a_file()
	{
		var song = SongFile.Load(File, Bindings);
		var verse = song.Sections[0];
		verse.Exits.Select(e => (e.When is null ? null : ConditionText.Write(e.When), e.To.Name))
			.ShouldBe([("pass < 2", "verse"), ("energy > 0.6", "chorus"), (null, "verse")]);
		song.Cues.ShouldContain(Energy);

		var written = SongFile.ToJson(song);
		var again = SongFile.Load(SongFile.Write(written), Bindings);
		again.Sections[0].Exits.Count.ShouldBe(3);
		again.Sections[1].Exits.ShouldHaveSingleItem().To.Name.ShouldBe("verse");
	}

	[Test]
	public void A_version_1_file_is_refused_with_how_to_move_it()
	{
		var old = File.Replace("sirrow-song/2", "kaldra-song/1", StringComparison.Ordinal);
		Should.Throw<FormatException>(() => SongFile.Load(old, Bindings)).Message.ShouldContain("\"exits\"");

		var withOrder = File.Replace("\"sections\": [", "\"order\": [\"verse\"],\n  \"sections\": [", StringComparison.Ordinal);
		Should.Throw<FormatException>(() => SongFile.Load(withOrder, Bindings)).Message.ShouldContain("\"exits\"");
	}

	[Test]
	public void A_file_under_the_old_name_reads_as_the_new()
	{
		var old = File.Replace(SongFile.Format, SongFile.FormatBefore, StringComparison.Ordinal);
		SongFile.Load(old, Bindings).Sections[0].Exits.Count.ShouldBe(3);
		SongFile.ToJson(SongFile.Load(old, Bindings))["format"]!.GetValue<string>().ShouldBe(SongFile.Format);
	}

	[Test]
	public void A_part_can_play_on_one_pass_alone()
	{
		var song = new Song("second pass") { Tempo = 120 };
		var click = song.Track("click", MusicRig.Click);
		var a = song.Section("a", 1);
		a.Play(click, Line.Quarters("1 1 1 1")).When(Pass.Is(2));
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(2 * MusicRig.Bar);

		rig.Rms(MusicRig.Beat / 4, MusicRig.Bar - MusicRig.Beat / 2).ShouldBeLessThan(1e-4f);
		rig.Rms(MusicRig.Bar, MusicRig.Bar).ShouldBeGreaterThan(1e-3f);
	}

	[Test]
	public void A_placement_knows_its_pass_and_its_fill()
	{
		var song = new Song("passes") { Tempo = 120 };
		var click = song.Track("click", MusicRig.Click);
		var section = song.Section("a", 4);
		var placement = section.Play(click, Line.Quarters("1 1 1 1")).EndWith(Line.Quarters("5 5 5 5"));
		using var rig = new MusicRig();
		rig.Player.Play(song);

		rig.StepTo(MusicRig.Bar + MusicRig.Bar / 2);
		var middle = rig.Player.PositionOf(placement);
		(middle.IsPlaying, middle.Pass, middle.Passes, middle.InFill).ShouldBe((true, 1, 3, false));

		rig.StepTo(3 * MusicRig.Bar + MusicRig.Bar / 2);
		rig.Player.PositionOf(placement).InFill.ShouldBeTrue();

		var pattern = rig.Player.SectionPatternOf(placement);
		pattern.LengthTicks.ShouldBe(4 * pattern.TicksPerBar);
		pattern.FillTick.ShouldBe(3 * pattern.TicksPerBar);
		pattern.Events.Count.ShouldBe(16);   // three passes of four, and the fill's four
	}

	[Test]
	public void A_tracks_sounding_notes_are_published()
	{
		var song = new Song("held") { Tempo = 120 };
		var tone = song.Track("tone", ChipRig.Tone);
		song.Section("a", 1).Play(tone, Line.Quarters("1 - - -"));
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(MusicRig.Bar / 4);

		var sounding = rig.Player.Sounding(tone);
		(sounding & (UInt128.One << 60)).ShouldNotBe(UInt128.Zero);   // C4, the scale's first degree
		UInt128.PopCount(sounding).ShouldBe(UInt128.One);
	}

	[Test]
	public void A_sections_chords_are_given_bar_by_bar_as_written()
	{
		var song = new Song("chords");
		var section = song.Section("a", 4, "i | VI VII | IV");
		section.Progression.Select(b => string.Join(" ", b.Written)).ShouldBe(["i", "VI VII", "IV"]);
		section.Progression[1].Chords.Select(c => c.Degree).ShouldBe([6, 7]);
	}
}
