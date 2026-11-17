using System.Diagnostics;

using Loom.Audio;
using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Effects;
using Loom.Audio.Synth.Music;

namespace Loom.Tests;

/// <summary>The player the game talks to: position, scale, swapping, cues, crossfades, a conductor's faults, and its cost.</summary>
public class MusicPlayerTests
{
	private static Song Held(string name, string note, int bars = 1)
	{
		var song = new Song(name);
		var track = song.Track("tone", ChipRig.Tone);
		song.Section("a", bars).Play(track, Line.Quarters($"{note} - - -"));
		return song;
	}

	[Test]
	public void Its_position_is_the_bar_and_beat()
	{
		using var rig = new MusicRig();
		rig.Player.Play(Held("held", "1", bars: 4));
		rig.Step(MusicRig.Bar * 5 / 2);

		var position = rig.Player.Position;
		position.Bar.ShouldBe(3);
		position.Beat.ShouldBe(3);
		position.AbsoluteBar.ShouldBe(2);
		position.Section.ShouldBe("a");
	}

	[Test]
	public void A_song_plays_in_another_scale()
	{
		using var rig = new MusicRig();
		rig.Player.Play(Held("held", "1"), Scale.Major(Note.G));
		rig.Step(MusicRig.Bar);

		rig.Pitch(MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(392, 4);
	}

	[Test]
	public void A_swap_takes_effect_at_the_next_bar_line()
	{
		using var rig = new MusicRig();
		rig.Player.Play(Held("held", "1"));
		rig.Step(MusicRig.Bar + MusicRig.Bar / 2);
		rig.Player.Swap(Held("held", "8"));
		rig.Step(MusicRig.Bar + MusicRig.Bar / 2);

		rig.Pitch(MusicRig.Bar + MusicRig.Bar / 8, MusicRig.Bar / 4).ShouldBe(261.6, 3);
		rig.Pitch(2 * MusicRig.Bar + MusicRig.Bar / 4, MusicRig.Bar / 2).ShouldBe(523.3, 5);
		rig.Player.Position.AbsoluteBar.ShouldBe(2);
	}

	private static Song Grouped(string name, float outputDb, bool second)
	{
		var song = new Song(name);
		var loud = song.Group("loud", Fx.Distortion(driveDb: 0f, outputDb: outputDb));
		var tone = song.Track("tone", ChipRig.Tone, loud);
		var section = song.Section("a", 1);
		section.Play(tone, Line.Quarters("1 - - -"));
		if (second)
		{
			var other = song.Group("other", Fx.Distortion(driveDb: 0f, outputDb: 0f));
			section.Play(song.Track("high", ChipRig.Tone, other), Line.Quarters("5 - - -"));
		}
		return song;
	}

	[Test]
	public void A_swap_brings_the_new_songs_groups()
	{
		using var rig = new MusicRig();
		rig.Player.Play(Grouped("grouped", -6f, second: false));
		rig.Step(MusicRig.Bar / 2);
		rig.Player.Swap(Grouped("grouped", -6f, second: true));
		Should.NotThrow(() => rig.Step(MusicRig.Bar * 2));

		// The track the swap added plays through the group it added: the fifth sounds beside the root
		var start = 2 * MusicRig.Bar - MusicRig.Bar / 2;
		rig.Level(start, MusicRig.Bar / 2, 392f).ShouldBeGreaterThan(0.01f);
		rig.Level(start, MusicRig.Bar / 2, 261.6f).ShouldBeGreaterThan(0.01f);
	}

	[Test]
	public void A_swap_takes_the_new_songs_effect_settings()
	{
		using var rig = new MusicRig();
		rig.Player.Play(Grouped("grouped", -20f, second: false));
		rig.Step(MusicRig.Bar / 2);
		var before = rig.Rms(MusicRig.Bar / 8, MusicRig.Bar / 4);
		rig.Player.Swap(Grouped("grouped", 0f, second: false));
		rig.Step(MusicRig.Bar * 2);

		// 20 dB louder through the rebuilt group's distortion, its old chip long rung out
		var after = rig.Rms(MusicRig.Bar + MusicRig.Bar / 8, MusicRig.Bar / 4);
		(after / before).ShouldBeGreaterThan(5f);
	}

	[Test]
	public void A_cue_set_before_play_is_in_force_from_the_first_bar()
	{
		var cue = new Cue("energy");
		var song = new Song("cued");
		var track = song.Track("click", MusicRig.Click);
		song.Section("a", 1).Play(track, Line.Quarters("1 1 1 1")).When(cue.Above(0.5f));
		using var rig = new MusicRig();
		rig.Player.Set(cue, 1f);
		rig.Player.Play(song);
		rig.Step(MusicRig.Bar / 2);

		MusicRig.Onsets(rig.Left())[0].ShouldBeLessThanOrEqualTo(16);
	}

	[Test]
	public void A_section_sweeps_a_macro_from_its_first_bar_to_its_last()
	{
		// A saw under a low-pass that a macro opens from 100 Hz to 6.4 kHz, swept across a section of two bars; then a
		// section that doesn't sweep it, where it is back as the track has it (shut)
		var macro = new Macro(0);
		var bright = Patch.Build("bright", p =>
		{
			p.Out(p.Wave(Waveform.Saw, 1f, Env.Gate, 0.5f));
			p.Filter(Filter.LowPass(100f));
			p.Macro(macro, MacroTarget.FilterCutoff, 1f, 64f);
		});
		var song = new Song("sweep") { Tempo = 120 };
		var track = song.Track("saw", bright);
		var build = song.Section("build", 2);
		build.Play(track, Line.Quarters("1 1 1 1"));
		build.Sweep(track, macro, 0f, 1f);
		var after = song.Section("after", 1);
		after.Play(track, Line.Quarters("1 1 1 1"));
		song.Order(build, after);

		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(3 * MusicRig.Bar);

		// Louder as the filter opens: by halfway it is past everything the saw has, and it stays open to the end
		var first = rig.Rms(0, MusicRig.Beat);
		var middle = rig.Rms(4 * MusicRig.Beat, MusicRig.Beat);
		var last = rig.Rms(7 * MusicRig.Beat, MusicRig.Beat);
		middle.ShouldBeGreaterThan(1.3f * first);
		last.ShouldBeGreaterThan(0.98f * middle);
		// And shut in the section after, further than in the first beat, during which it was already opening
		rig.Rms(9 * MusicRig.Beat, MusicRig.Beat).ShouldBeLessThan(0.6f * first);
	}

	[Test]
	public void A_new_song_crossfades_over_the_last()
	{
		using var rig = new MusicRig();
		rig.Player.Play(Held("first", "1"));
		rig.Step(MusicRig.Bar / 2);
		rig.Player.Play(Held("second", "5"), fadeSeconds: 0.5f);
		rig.Step(MusicRig.Rate / 4);
		rig.Session.Engine.Stats.ActiveVoices.ShouldBe(2);
		rig.Step(MusicRig.Rate);
		rig.Session.Engine.Stats.ActiveVoices.ShouldBe(1);
	}

	[Test]
	public void A_conductor_that_throws_is_thrown_from_update_and_the_section_loops()
	{
		var song = Held("faulty", "1");
		song.Conduct(_ => throw new InvalidDataException("on purpose"));
		using var rig = new MusicRig();
		rig.Player.Play(song);

		Should.Throw<InvalidOperationException>(() => rig.Step(MusicRig.Bar)).InnerException.ShouldBeOfType<InvalidDataException>();
		rig.Step(MusicRig.Bar / 2);
		rig.Player.Position.Section.ShouldBe("a");
		rig.Player.Position.AbsoluteBar.ShouldBe(1);
	}

	private static Song Busy()
	{
		var song = new Song("busy");
		var drums = song.Track("drums", DrumKit.FromPatches("kit", (Kit.Kick, MusicRig.Click, 0), (Kit.HatClosed, MusicRig.Click, 1)));
		var lead = song.Track("lead", ChipRig.Tone).Humanise(4f, 0.1f);
		var section = song.Section("a", 2, "I | IV");
		section.Play(drums, Beat.Eighths((Kit.Kick, "x...x..."), (Kit.HatClosed, "xxxxxxxx")));
		section.Play(lead, Line.Eighths("1 3 5 3 1 3 5 8").OverChords());
		return song;
	}

	[Test]
	public void Rendered_ahead_a_song_is_the_same_samples_as_live()
	{
		using var live = new MusicRig();
		using var ahead = new MusicRig(aheadSeconds: 0.1);
		live.Player.Play(Busy());
		ahead.Player.Play(Busy());
		live.Step(2 * MusicRig.Bar);
		ahead.Step(2 * MusicRig.Bar);

		var a = live.Left();
		var b = ahead.Left();
		a.Length.ShouldBe(b.Length);
		for (var i = 0; i < a.Length; i++)
			b[i].ShouldBe(a[i], $"frame {i}");
		live.Rms(0, a.Length).ShouldBeGreaterThan(0.01f);   // not two silences alike
	}

	// A click that comes in with a cue: the bar line it first sounds on, live or rendered ahead, the cue set at a frame
	private static int FirstOnsetAfterCue(double aheadSeconds, int setAt)
	{
		var cue = new Cue("energy");
		var song = new Song("cued");
		var track = song.Track("click", MusicRig.Click);
		song.Section("a", 1).Play(track, Line.Quarters("1 1 1 1")).When(cue.Above(0.5f));
		using var rig = new MusicRig(aheadSeconds);
		rig.Player.Play(song);
		rig.StepTo(setAt);
		rig.Player.Set(cue, 1f);
		rig.Step(3 * MusicRig.Bar);
		return MusicRig.Onsets(rig.Left())[0];
	}

	[Test]
	public void Rendered_ahead_a_cue_takes_effect_at_a_bar_line_no_more_than_the_lead_later()
	{
		const double lead = 0.1;
		var leadFrames = (int)(lead * MusicRig.Rate);
		foreach (var setAt in (int[])[MusicRig.Bar / 2, MusicRig.Bar - 1000])
		{
			var live = FirstOnsetAfterCue(0, setAt);
			var ahead = FirstOnsetAfterCue(lead, setAt);
			// Live, the next bar line after the cue; ahead, the next after the cue and the lead
			var liveBar = (setAt + MusicRig.Bar - 1) / MusicRig.Bar * MusicRig.Bar;
			var aheadBar = (setAt + leadFrames + 256 + MusicRig.Bar - 1) / MusicRig.Bar * MusicRig.Bar;
			live.ShouldBeInRange(liveBar, liveBar + 16);
			ahead.ShouldBeInRange(aheadBar, aheadBar + 16);
			(ahead - live).ShouldBeLessThanOrEqualTo(MusicRig.Bar + 16);
		}
	}

	[Test]
	public void Steady_play_allocates_nothing()
	{
		var cue = new Cue("energy", 1f);
		var song = new Song("steady");
		var drums = song.Track("drums", DrumKit.FromPatches("kit", (Kit.Kick, MusicRig.Click, 0), (Kit.HatClosed, MusicRig.Click, 1)));
		var lead = song.Track("lead", ChipRig.Tone).Humanise(4f, 0.1f);
		lead.Macro(new Macro(0)).Follow(cue);
		var section = song.Section("a", 2, "I | IV");
		section.Play(drums, Beat.Eighths((Kit.Kick, "x...x..."), (Kit.HatClosed, "xxxxxxxx")));
		section.Play(lead, Line.Eighths("1 3 5 3 1 3 5 8").OverChords()).When(cue.Above(0.5f)).Level(cue);
		var device = new OfflineAudioDevice(MusicRig.Rate, 256);
		using var engine = new AudioEngine(device);
		using var player = new MusicPlayer(engine, engine.Master);
		player.Play(song);
		var output = new float[512];

		void Block(int i)
		{
			player.Set(cue, 0.6f + (i % 3) * 0.1f);
			player.Update();
			device.Render(output);
			engine.Update();
		}

		for (var i = 0; i < 64; i++)
			Block(i);
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 512; i++)
			Block(i);
		(GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
	}

	[Test]
	public void An_eight_track_song_renders_many_times_faster_than_real_time()
	{
		var song = new Song("busy") { Scale = Scale.Minor(Note.A), Tempo = 128 };
		var drums = song.Track("drums", DrumKit.FromPatches("kit",
			(Kit.Kick, Patch.Build("kick", p => { p.Out(p.Sine(1f, Env.Decay(0.25f)).Fixed(50f)); p.PitchDrop(24f, 0.04f); }), 0),
			(Kit.Snare, Patch.Build("snare", p => p.Out(p.Noise(Env.Decay(0.16f), 0.8f))), 0),
			(Kit.HatClosed, Patch.Build("hat", p => p.Out(p.Noise(Env.Decay(0.04f), 0.5f))), 1)));
		var bass = song.Track("bass", Patch.Build("bass", p =>
		{
			var m = p.Sine(3f, Env.Decay(0.06f), 0.8f);
			p.Out(p.Sine(1f, Env.Adsr(0.002f, 0.9f, 0.3f, 0.08f)).Feedback(0.15f).ModulatedBy(m, 2f));
		})).Octave(2);
		var section = song.Section("a", 4, "i | VI | III | VII");
		section.Play(drums, Beat.Eighths((Kit.Kick, "x...x..."), (Kit.Snare, "..x...x."), (Kit.HatClosed, "xxxxxxxx")));
		section.Play(bass, Line.Eighths("1 1 . 1 5, 1 . 1").OverChords());
		for (var i = 0; i < 6; i++)
		{
			var track = song.Track($"pad{i}", Patch.Build($"pad{i}", p =>
			{
				var m2 = p.Sine(3f, Env.Adsr(0.01f, 0.3f, 0.4f, 0.2f));
				var c2 = p.Sine(1f, Env.Adsr(0.01f, 0.3f, 0.7f, 0.2f)).ModulatedBy(m2);
				var m1 = p.Sine(1f, Env.Adsr(0.01f, 0.5f, 0.3f, 0.2f)).Feedback(0.3f);
				var c1 = p.Sine(2f, Env.Adsr(0.01f, 0.5f, 0.6f, 0.2f)).ModulatedBy(m1);
				p.Out(c1.At(0.3f), c2.At(0.3f));
			})).Octave(4 + i % 2);
			section.Play(track, Line.Eighths("1 3 5 3 2 4 6 4").OverChords());
		}
		using var rig = new MusicRig();
		rig.Player.Play(song);
		rig.Step(2 * MusicRig.Rate);   // warm: every path compiled, and promoted by the tiered JIT
		var clock = Stopwatch.StartNew();
		rig.Step(10 * MusicRig.Rate);
		var speed = 10 / clock.Elapsed.TotalSeconds;

		Console.WriteLine($"Eight tracks: {speed:F1}x real time");
		speed.ShouldBeGreaterThan(20);
	}
}
