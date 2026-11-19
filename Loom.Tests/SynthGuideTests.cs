using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Effects;
using Loom.Audio.Synth.Music;

namespace Loom.Tests;

/// <summary>The music sample in docs/synth.md, copied here verbatim, builds and plays: the guide can't drift from the API.</summary>
public class SynthGuideTests
{
	// docs/synth.md, section 9, as written there
	static class Groove
	{
		// What the game tells the song, 0 to 1, and a macro its guitar patch turns: made once, since a cue is known by its object
		public static readonly Cue Energy = new("energy");
		public static readonly Macro Drive = new(1);

		public static Song Build(DrumKit kit, Patch bass, Patch guitar)
		{
			var song = new Song("groove") { Tempo = 128, Scale = Scale.Minor(Note.E) };
			var guitars = song.Group("guitars", Fx.Distortion(24, cabinet: Cabinet.FourByTwelve));
			var drums = song.Track("drums", kit).Humanise(4, 0.1f);
			var low = song.Track("bass", bass).Octave(2);
			var rhythm = song.Track("rhythm", guitar, guitars).Octave(3);
			rhythm.Macro(Drive).Follow(Energy, 0.2f, 1f);

			var verse = song.Section("verse", 4, "i | VI | III | VII");
			verse.Play(drums, Beat.Eighths((Kit.Kick, "x... x.x."), (Kit.Snare, "..x. ..x."), (Kit.HatClosed, "xxxx xxxx")))
				.EndWith(Beat.Sixteenths((Kit.TomHigh, "xxxx .... .... ...."), (Kit.TomLow, ".... xxxx .... ...."), (Kit.Snare, ".... .... ..xX xxXX")));
			verse.Play(low, Line.Eighths("1 1 . 1 5, 1 . 1").OverChords());
			verse.Play(rhythm, Strum.Eighths("D.DU .UDU").Voicing(Voicing.Power)).When(Energy.Above(0.4f));

			var chorus = song.Section("chorus", 4, "VI VII | i | VI VII | i");
			chorus.Play(drums, Beat.Eighths((Kit.Kick, "x.x. x.x."), (Kit.Snare, "..X. ..X."), (Kit.Crash, "x... ....")));
			chorus.Play(low, Line.Eighths("1 - 1 - 1 1 5, 1").OverChords());
			verse.Exit(verse, Pass.Below(2));
			verse.Then(chorus);
			chorus.Then(verse);
			return song;
		}
	}

	private static DrumKit Kit5 => DrumKit.FromPatches("kit",
		(Kit.Kick, MusicRig.Click, 0), (Kit.Snare, MusicRig.Click, 0), (Kit.HatClosed, MusicRig.Click, 1),
		(Kit.TomHigh, MusicRig.Click, 0), (Kit.TomLow, MusicRig.Click, 0), (Kit.Crash, MusicRig.Click, 0));

	[Test]
	public void The_guides_song_builds_and_plays()
	{
		using var rig = new MusicRig();
		rig.Player.Set(Groove.Energy, 0.7f);
		rig.Player.Play(Groove.Build(Kit5, ChipRig.Tone, ChipRig.Tone));
		rig.Step(4 * MusicRig.Bar);

		rig.Rms(0, 4 * MusicRig.Bar).ShouldBeGreaterThan(0.01f);
		rig.Player.Position.AbsoluteBar.ShouldBeGreaterThanOrEqualTo(3);
	}
}
