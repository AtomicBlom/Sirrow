using Loom.Audio.Synth.Dsp;
using Loom.Audio.Synth.Music;

namespace Loom.Tests;

/// <summary>The sequencer, heard: notes on the clock, parts coming and going on cues, sections in turn, fills, mutes, humanising and swing.</summary>
public class SequencerTests
{
	private static void ShouldBeNear(int actual, int expected, int within = 16) =>
		Math.Abs(actual - expected).ShouldBeLessThanOrEqualTo(within, $"onset at {actual}, expected {expected}");

	private static (Song Song, SongTrack Track, Section Section) Clicks(int bars = 1)
	{
		var song = new Song("clicks");
		var track = song.Track("click", MusicRig.Click);
		var section = song.Section("a", bars);
		return (song, track, section);
	}

	[Test]
	public void Quarter_notes_land_on_the_beats()
	{
		var (song, track, section) = Clicks();
		section.Play(track, Line.Quarters("1 1 1 1"));
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(MusicRig.Bar + 10_000);

		var onsets = MusicRig.Onsets(rig.Left());
		onsets.Count.ShouldBe(5);
		for (var i = 0; i < 5; i++)
			ShouldBeNear(onsets[i], i * MusicRig.Beat);
	}

	[Test]
	public void A_part_comes_in_at_the_bar_after_its_cue()
	{
		var cue = new Cue("energy");
		var (song, track, section) = Clicks();
		section.Play(track, Line.Quarters("1 1 1 1")).When(cue.Above(0.5f));
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(40_000);
		rig.Player.Set(cue, 1f);
		rig.Step(MusicRig.Bar);

		ShouldBeNear(MusicRig.Onsets(rig.Left())[0], MusicRig.Bar);
	}

	[Test]
	public void An_on_phrase_part_comes_in_where_it_starts_over()
	{
		var cue = new Cue("energy");
		var (song, track, section) = Clicks(bars: 8);
		section.Play(track, Line.Quarters("1 1 1 1 | 1 1 1 1 | 1 1 1 1 | 1 1 1 1")).When(cue.Above(0.5f)).OnPhrase();
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(100_000);
		rig.Player.Set(cue, 1f);
		rig.Step(4 * MusicRig.Bar);

		ShouldBeNear(MusicRig.Onsets(rig.Left())[0], 4 * MusicRig.Bar);
	}

	[Test]
	public void An_on_bar_part_stops_at_the_bar_after_its_cue_drops()
	{
		var cue = new Cue("energy", 1f);
		var (song, track, section) = Clicks();
		section.Play(track, Line.Quarters("1 1 1 1")).When(cue.Above(0.5f));
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(MusicRig.Bar + 20_000);
		rig.Player.Set(cue, 0f);
		rig.Step(2 * MusicRig.Bar);

		MusicRig.Onsets(rig.Left()).ShouldAllBe(o => o < 2 * MusicRig.Bar);
	}

	[Test]
	public void A_condition_holds_through_a_hovering_cue()
	{
		var cue = new Cue("energy", 1f);
		var (song, track, section) = Clicks();
		section.Play(track, Line.Quarters("1 1 1 1")).When(cue.Above(0.5f));
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(MusicRig.Bar / 2);
		rig.Player.Set(cue, 0.47f);
		rig.Step(MusicRig.Bar);
		MusicRig.Onsets(rig.Left()).ShouldContain(o => Math.Abs(o - MusicRig.Bar) <= 16);

		rig.Player.Set(cue, 0.44f);
		rig.Step(MusicRig.Bar);
		MusicRig.Onsets(rig.Left()).ShouldAllBe(o => o < 2 * MusicRig.Bar);
	}

	[Test]
	public void A_parts_level_follows_its_cue()
	{
		var cue = new Cue("energy", 1f);
		var song = new Song("level");
		var track = song.Track("tone", ChipRig.Tone);
		song.Section("a", 1).Play(track, Line.Eighths("1 1 1 1 1 1 1 1")).Level(cue);
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(MusicRig.Bar);
		var loud = rig.Rms(0, MusicRig.Bar);
		rig.Player.Set(cue, 0f);
		rig.Step(MusicRig.Rate + MusicRig.Bar);

		var quiet = rig.Rms(MusicRig.Bar + MusicRig.Rate, MusicRig.Bar);
		loud.ShouldBeGreaterThan(20f * quiet);
	}

	[Test]
	public void Sections_follow_their_exits()
	{
		var song = new Song("exits");
		var track = song.Track("tone", ChipRig.Tone);
		var a = song.Section("a", 1);
		a.Play(track, Line.Quarters("1 - - -"));
		var b = song.Section("b", 1);
		b.Play(track, Line.Quarters("8 - - -"));
		a.Then(b);
		b.Then(a);
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(3 * MusicRig.Bar);

		rig.Pitch(MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(261.6, 3);
		rig.Pitch(MusicRig.Bar + MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(523.3, 5);
		rig.Pitch(2 * MusicRig.Bar + MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(261.6, 3);
	}

	[Test]
	public void An_exit_on_a_cue_chooses_the_next_section()
	{
		var cue = new Cue("energy");
		var song = new Song("cued exits");
		var track = song.Track("tone", ChipRig.Tone);
		var a = song.Section("a", 1);
		a.Play(track, Line.Quarters("1 - - -"));
		var b = song.Section("b", 1);
		b.Play(track, Line.Quarters("8 - - -"));
		a.Exit(b, cue.Above(0.5f));
		b.Exit(b, cue.Above(0.5f));
		b.Then(a);
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(2 * MusicRig.Bar + MusicRig.Bar / 2);
		rig.Player.Set(cue, 1f);
		rig.Step(MusicRig.Bar + MusicRig.Bar / 2);

		rig.Pitch(MusicRig.Bar + MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(261.6, 3);
		rig.Pitch(2 * MusicRig.Bar + MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(261.6, 3);
		rig.Pitch(3 * MusicRig.Bar + MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(523.3, 5);
	}

	[Test]
	public void A_fill_takes_the_sections_last_bar()
	{
		var song = new Song("fill");
		var track = song.Track("tone", ChipRig.Tone);
		song.Section("a", 2).Play(track, Line.Quarters("1 - - -")).EndWith(Line.Quarters("5 - - -"));
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(2 * MusicRig.Bar);

		rig.Pitch(MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(261.6, 3);
		rig.Pitch(MusicRig.Bar + MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(392.0, 4);
	}

	[Test]
	public void Mute_solo_and_force()
	{
		static (Song, SongTrack, SongTrack, Placement) Two()
		{
			var song = new Song("two");
			var click = song.Track("click", MusicRig.Click);
			var other = song.Track("other", MusicRig.Click);
			var section = song.Section("a", 1);
			var placement = section.Play(click, Line.Quarters("1 1 1 1"));
			section.Play(other, Line.Quarters(". . . ."));
			return (song, click, other, placement);
		}

		using (var rig = new MusicRig())
		{
			var (song, click, _, _) = Two();
			rig.Player.Play(song);
			rig.Player.Mute(click, true);
			rig.Step(MusicRig.Bar);
			MusicRig.Onsets(rig.Left()).ShouldBeEmpty();
		}
		using (var rig = new MusicRig())
		{
			var (song, _, other, _) = Two();
			rig.Player.Play(song);
			rig.Player.Solo(other, true);
			rig.Step(MusicRig.Bar);
			MusicRig.Onsets(rig.Left()).ShouldBeEmpty();
		}
		using (var rig = new MusicRig())
		{
			var song = new Song("forced");
			var click = song.Track("click", MusicRig.Click);
			var placement = song.Section("a", 1).Play(click, Line.Quarters("1 1 1 1"));
			rig.Player.Play(song);
			rig.Step(MusicRig.Bar / 2);
			rig.Player.Force(placement, PlacementState.Off);
			rig.Step(MusicRig.Bar);
			MusicRig.Onsets(rig.Left()).ShouldAllBe(o => o < MusicRig.Bar);
			rig.Player.Force(placement, PlacementState.On);
			rig.Step(MusicRig.Bar);
			MusicRig.Onsets(rig.Left()).ShouldContain(o => Math.Abs(o - 2 * MusicRig.Bar) <= 16);
		}
	}

	private static float[] Humanised(bool humanise)
	{
		var song = new Song("loose");
		var track = song.Track("click", MusicRig.Click);
		if (humanise)
			track.Humanise(6f, 0.1f);
		song.Section("a", 2).Play(track, Line.Quarters("1 1 1 1"));
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(2 * MusicRig.Bar);
		return rig.Left();
	}

	[Test]
	public void Humanising_only_delays_and_plays_the_same_every_time()
	{
		var straight = MusicRig.Onsets(Humanised(false));
		var loose = MusicRig.Onsets(Humanised(true));

		loose.Count.ShouldBe(straight.Count);
		for (var i = 0; i < straight.Count; i++)
		{
			loose[i].ShouldBeGreaterThanOrEqualTo(straight[i]);
			(loose[i] - straight[i]).ShouldBeLessThanOrEqualTo(600);
		}
		Humanised(true).ShouldBe(Humanised(true));
	}

	[Test]
	public void Swing_moves_the_second_eighth()
	{
		var song = new Song("swing");
		var track = song.Track("click", MusicRig.Click).Swing(0.667f);
		song.Section("a", 1).Play(track, Line.Eighths("1 1 . . . . . ."));
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(MusicRig.Bar / 2);

		ShouldBeNear(MusicRig.Onsets(rig.Left())[1], 16_000);
	}
}
