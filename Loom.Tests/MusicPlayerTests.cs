using System.Diagnostics;

using Loom.Audio;
using Loom.Audio.Synth.Chip;
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
