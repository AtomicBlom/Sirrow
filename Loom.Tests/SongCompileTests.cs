using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Music;

namespace Loom.Tests;

/// <summary>A song made into the sequencer's arrays: notes, swing, loop lengths, cues and scales.</summary>
public class SongCompileTests
{
	[Test]
	public void Swing_pushes_the_off_beats()
	{
		var song = new Song("swing");
		var track = song.Track("lead", ChipRig.Tone).Swing(0.667f);
		song.Section("a", 1).Play(track, Line.Eighths("1 1 1 1 1 1 1 1"));

		var ticks = CompiledSong.Compile(song, song.Scale).Sections[0].Placements[0].Events.Select(e => e.Tick).ToArray();

		ticks[1].ShouldBe(640);
		ticks[2].ShouldBe(960);
	}

	[Test]
	public void A_one_bar_part_in_a_four_bar_section_loops_a_bar()
	{
		var song = new Song("loop");
		var track = song.Track("lead", ChipRig.Tone);
		song.Section("a", 4).Play(track, Line.Quarters("1 2 3 4"));

		var placement = CompiledSong.Compile(song, song.Scale).Sections[0].Placements[0];

		placement.LoopTicks.ShouldBe(3840);
		placement.Events.Length.ShouldBe(16);
	}

	[Test]
	public void Each_cue_is_listed_once()
	{
		var energy = new Cue("energy");
		var calm = new Cue("calm");
		var song = new Song("cues");
		var track = song.Track("lead", ChipRig.Tone);
		var section = song.Section("a", 1);
		section.Play(track, Line.Quarters("1 2 3 4")).When(energy.Above(0.5f) & !calm.Above(0.2f)).Level(energy);
		section.Play(track, Line.Quarters("5 4 3 2")).When(energy.Below(0.5f));

		CompiledSong.Compile(song, song.Scale).Cues.ShouldBe([energy, calm]);
	}

	[Test]
	public void Another_scale_moves_every_note_and_no_hit()
	{
		var song = new Song("scales");
		var lead = song.Track("lead", ChipRig.Tone);
		var drums = song.Track("drums", DrumKit.FromPatches("kit", (Kit.Kick, ChipRig.Tone, 0)));
		var section = song.Section("a", 1);
		section.Play(lead, Line.Quarters("1 3 5 8"));
		section.Play(drums, Beat.Eighths((Kit.Kick, "x.x.x.x.")));

		var inC = CompiledSong.Compile(song, Scale.Major(Note.C)).Sections[0];
		var inD = CompiledSong.Compile(song, Scale.Major(Note.D)).Sections[0];

		inD.Placements[0].Events.Select(e => e.Note - 2).ShouldBe(inC.Placements[0].Events.Select(e => e.Note));
		inD.Placements[1].Events.Select(e => (e.Tick, e.Piece)).ShouldBe(inC.Placements[1].Events.Select(e => (e.Tick, e.Piece)));
	}

	[Test]
	public void A_tempo_of_zero_is_refused()
	{
		var song = new Song("still") { Tempo = 0f };
		song.Section("a", 1).Play(song.Track("tone", ChipRig.Tone), Line.Quarters("1 - - -"));
		Should.Throw<InvalidOperationException>(() => CompiledSong.Compile(song, song.Scale)).Message.ShouldContain("tempo");
	}

	[Test]
	public void A_condition_nested_deeper_than_the_sequencer_holds_is_refused()
	{
		var cue = new Cue("c");
		Condition deep = cue.Above(0.5f);
		for (var i = 0; i < CompiledSong.ConditionDepth; i++)
			deep = cue.Above(0.1f * (i % 9)) & deep;
		var song = new Song("deep");
		song.Section("a", 1).Play(song.Track("tone", ChipRig.Tone), Line.Quarters("1 - - -")).When(deep);
		Should.Throw<InvalidOperationException>(() => CompiledSong.Compile(song, song.Scale)).Message.ShouldContain("deeper");
	}

	[Test]
	public void A_swung_strum_moves_all_its_strings_together()
	{
		var song = new Song("swung");
		var guitar = song.Track("guitar", ChipRig.Tone).Swing(0.667f);
		song.Section("a", 1, "I").Play(guitar, Strum.Eighths("DU.. ....").Voicing(Voicing.Triad));
		var events = CompiledSong.Compile(song, song.Scale).Sections[0].Placements[0].Events;

		// The up stroke is on the second eighth (480), swung to 640: every one of its strings starts at or after it, spread
		// as an unswung stroke's are
		var up = events.Where(e => e.Tick >= 480).Select(e => e.Tick).ToArray();
		up.Length.ShouldBe(4);   // a triad laid as a guitar strums it: root, fifth, octave and the third above
		up.Min().ShouldBe(640);
		(up.Max() - up.Min()).ShouldBeLessThan(100);
	}

	[Test]
	public void A_song_is_fixed_once_compiled()
	{
		var song = new Song("fixed");
		var tone = song.Track("tone", ChipRig.Tone);
		var section = song.Section("a", 1);
		var placement = section.Play(tone, Line.Quarters("1 - - -"));
		CompiledSong.Compile(song, song.Scale);

		Should.Throw<InvalidOperationException>(() => song.Tempo = 90f).Message.ShouldContain("fixed");
		Should.Throw<InvalidOperationException>(() => song.Track("late", ChipRig.Tone));
		Should.Throw<InvalidOperationException>(() => section.Play(tone, Line.Quarters("5 - - -")));
		Should.Throw<InvalidOperationException>(() => tone.Level(-6f));
		Should.Throw<InvalidOperationException>(() => tone.Macro(new Loom.Audio.Synth.Chip.Macro(0)));
		Should.Throw<InvalidOperationException>(() => placement.Named("late"));
		// Compiling it again is reading it: allowed
		Should.NotThrow(() => CompiledSong.Compile(song, song.Scale));
	}

	[Test]
	public void A_meter_set_after_a_part_is_placed_is_refused()
	{
		var song = new Song("metered");
		song.Section("a", 1).Play(song.Track("tone", ChipRig.Tone), Line.Quarters("1 - - -"));

		Should.Throw<InvalidOperationException>(() => song.Meter = new Meter(3, 4)).Message.ShouldContain("before placing");
	}
}
