using Loom.Audio;
using Loom.Audio.Synth;
using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Dsp;
using Loom.Audio.Synth.Effects;

namespace Loom.Tests;

/// <summary>The synthesizer chip, played on an offline session: pitch and level, modulation, notes ending, stealing, mono, groups, sends, macros, kits, samples, and the audio thread's rules.</summary>
public class ChipTests
{
	[Test]
	public void A_tone_plays_at_its_pitch_and_level()
	{
		using var rig = new ChipRig();
		rig.Chip.NoteOn(rig.Chip.AddInstrument(ChipRig.Tone), 69, velocity: 1f);
		rig.Seconds(1.1);

		rig.Pitch(0.1, 1.0).ShouldBe(440, 0.5);
		AudioAnalysis.LevelAt(rig.Left(0.1, 1.0), 440, ChipRig.Rate).ShouldBe(0.7071f, 0.02f);
		AudioAnalysis.LevelAt(rig.Right(0.1, 1.0), 440, ChipRig.Rate).ShouldBe(0.7071f, 0.02f);
	}

	[Test]
	public void Half_the_velocity_is_a_quarter_of_the_level()
	{
		using var rig = new ChipRig();
		rig.Chip.NoteOn(rig.Chip.AddInstrument(ChipRig.Tone), 69, velocity: 0.5f);
		rig.Seconds(1.1);

		AudioAnalysis.LevelAt(rig.Left(0.1, 1.0), 440, ChipRig.Rate).ShouldBe(0.7071f / 4f, 0.01f);
	}

	[Test]
	public void Phase_modulation_makes_the_bessel_sidebands()
	{
		using var rig = new ChipRig();
		var fm = rig.Chip.AddInstrument(Patch.Build("fm", p =>
		{
			var modulator = p.Sine(1f, Env.Gate);
			p.Out(p.Sine(1f, Env.Gate).ModulatedBy(modulator, 2f));
		}));
		rig.Chip.NoteOn(fm, 69, velocity: 1f);
		rig.Seconds(1.1);

		var left = rig.Left(0.1, 1.0);
		var ratio = AudioAnalysis.LevelAt(left, 880, ChipRig.Rate) / AudioAnalysis.LevelAt(left, 440, ChipRig.Rate);
		ratio.ShouldBe(5.47f, 0.5f);
	}

	[Test]
	public void A_held_note_sounds_on_after_its_click_has_died()
	{
		// A held tone with a 50 ms struck click beside it: once the click's envelope has gone idle it is skipped, and the
		// tone goes on at its own level, the click left nothing behind
		using var rig = new ChipRig();
		var clicked = rig.Chip.AddInstrument(Patch.Build("clicked", p =>
		{
			var modulator = p.Sine(3f, Env.Decay(0.05f));
			p.Out(p.Sine(1f, Env.Gate).ModulatedBy(modulator, 1f), p.Noise(Env.Decay(0.05f), 0.5f));
		}));
		rig.Chip.NoteOn(clicked, 69, velocity: 1f);
		rig.Seconds(2.1);

		// The modulator at three times the note puts a sideband at 1760 Hz while it lives, and none after
		AudioAnalysis.LevelAt(rig.Left(0.0, 0.02), 1760, ChipRig.Rate).ShouldBeGreaterThan(0.02f);
		var late = rig.Left(1.0, 1.0);
		AudioAnalysis.LevelAt(late, 440, ChipRig.Rate).ShouldBe(0.7071f, 0.02f);
		AudioAnalysis.LevelAt(late, 1760, ChipRig.Rate).ShouldBeLessThan(1e-4f);
		AudioAnalysis.Rms(late).ShouldBe(0.5f, 0.02f);
	}

	[Test]
	public void A_note_let_go_dies_away_and_frees_its_voice()
	{
		using var rig = new ChipRig();
		var pad = rig.Chip.AddInstrument(Patch.Build("pad", p => p.Out(p.Sine(1f, Env.Adsr(0.01f, 0.1f, 0.5f, 0.2f)))));
		var note = rig.Chip.NoteOn(pad, 60, velocity: 1f);
		rig.Seconds(0.3);
		rig.Chip.NoteOff(note);
		rig.Session.Clear();
		rig.Seconds(0.6);

		rig.PeakBoth(0.5, 0.1).ShouldBeLessThan(0.001f);
		rig.Chip.Stats.ActiveVoices.ShouldBe(0);
	}

	[Test]
	public void A_note_played_for_a_time_ends_on_its_own()
	{
		using var rig = new ChipRig();
		rig.Chip.Play(rig.Chip.AddInstrument(ChipRig.Tone), 60, 1f, seconds: 0.25f);
		rig.Seconds(0.6);

		rig.PeakBoth(0.5, 0.1).ShouldBe(0f);
		rig.Chip.Stats.ActiveVoices.ShouldBe(0);
	}

	[Test]
	public void Notes_past_the_voices_steal_them()
	{
		using var rig = new ChipRig(new ChipOptions { Voices = 32 });
		var tone = rig.Chip.AddInstrument(ChipRig.Tone);
		for (var i = 0; i < 40; i++)
			rig.Chip.NoteOn(tone, 40 + i, velocity: 0.1f);
		var most = 0;
		for (var block = 0; block < 20; block++)
		{
			rig.Session.Render(256);
			most = Math.Max(most, rig.Chip.Stats.ActiveVoices);
		}

		most.ShouldBeLessThanOrEqualTo(32);
		rig.Chip.Stats.StolenVoices.ShouldBe(8);
	}

	[Test]
	public void A_mono_patch_moves_its_one_voice()
	{
		using var rig = new ChipRig();
		var lead = rig.Chip.AddInstrument(Patch.Build("lead", p =>
		{
			p.Out(p.Sine(1f, Env.Gate));
			p.Mono();
		}));
		rig.Chip.NoteOn(lead, 69, 1f);
		rig.Seconds(0.3);
		rig.Chip.NoteOn(lead, 76, 1f);
		rig.Session.Clear();
		rig.Seconds(0.6);

		rig.Chip.Stats.ActiveVoices.ShouldBe(1);
		rig.Pitch(0.05, 0.5).ShouldBe(Units.NoteToHz(76), 1.0);
	}

	private sealed class Halve : AudioEffect
	{
		public override void Process(Span<float> left, Span<float> right)
		{
			for (var i = 0; i < left.Length; i++)
			{
				left[i] *= 0.5f;
				right[i] *= 0.5f;
			}
		}
	}

	[Test]
	public void A_group_runs_its_effects_over_its_instruments()
	{
		float LevelOn(int group)
		{
			using var rig = new ChipRig(new ChipOptions { Groups = [new("plain"), new("halved", () => new Halve())] });
			rig.Chip.NoteOn(rig.Chip.AddInstrument(ChipRig.Tone, group), 69, 1f);
			rig.Seconds(1.1);
			return AudioAnalysis.LevelAt(rig.Left(0.1, 1.0), 440, ChipRig.Rate);
		}

		LevelOn(1).ShouldBe(LevelOn(0) / 2f, 0.005f);
	}

	[Test]
	public void The_space_send_rings_on_after_a_note()
	{
		float TailRms(float send)
		{
			using var rig = new ChipRig();
			var patch = Patch.Build("plucked", p =>
			{
				p.Out(p.Sine(1f, Env.Gate));
				p.Send(ChipSend.Space, send);
			});
			rig.Chip.Play(rig.Chip.AddInstrument(patch), 69, 1f, seconds: 0.1f);
			rig.Seconds(0.75);
			return rig.RmsLeft(0.6, 0.15);
		}

		TailRms(1f).ShouldBeGreaterThan(1e-4f);
		TailRms(0f).ShouldBeLessThan(1e-5f);
	}

	[Test]
	public void A_duck_turns_its_group_down_while_the_key_sounds_and_leaves_the_rest()
	{
		// The bass at 110 Hz on a ducked group; the kick at 2 kHz on the plain group, sending to the key
		(float Bass, float Kick) Levels(bool kick)
		{
			using var rig = new ChipRig(new ChipOptions { Groups = [new("plain"), new("bass", Fx.Duck(thresholdDb: -30f, ratio: 8f))] });
			var bass = rig.Chip.AddInstrument(Patch.Build("bass", p => p.Out(p.Sine(1f, Env.Gate, 0.5f))), group: 1);
			var key = rig.Chip.AddInstrument(Patch.Build("kick", p =>
			{
				p.Out(p.Sine(1f, Env.Gate, 0.5f));
				p.Send(ChipSend.Key, 1f);
			}));
			rig.Chip.NoteOn(bass, 45, 1f);
			if (kick)
				rig.Chip.NoteOn(key, 95, 1f);
			rig.Seconds(1.1);
			var heard = rig.Left(0.5, 0.5);
			return (AudioAnalysis.LevelAt(heard, 110, ChipRig.Rate), AudioAnalysis.LevelAt(heard, 1975.5, ChipRig.Rate));
		}

		var alone = Levels(kick: false);
		var ducked = Levels(kick: true);
		// The key, about -9 dB at the voice's centre pan, is over 20 dB above -30: an 8:1 ratio takes most of that off the bass
		AudioAnalysis.ToDb(ducked.Bass / alone.Bass).ShouldBeLessThan(-15f);
		ducked.Kick.ShouldBeGreaterThan(alone.Bass * 0.5f);
	}

	[Test]
	public void A_pitch_drop_bends_a_fixed_operator_too()
	{
		using var rig = new ChipRig();
		var kick = rig.Chip.AddInstrument(Patch.Build("kick", p =>
		{
			p.Out(p.Sine(1f, Env.Gate).Fixed(100f));
			p.PitchDrop(12f, 2f);
		}));
		rig.Chip.NoteOn(kick, 60, 1f);
		rig.Seconds(4.1);
		// An octave up as it starts, falling to 100 Hz
		rig.Pitch(0.0, 0.05, 50, 400).ShouldBeGreaterThan(170);
		rig.Pitch(3.5, 0.5, 50, 400).ShouldBe(100, 3);
	}

	[Test]
	public void A_macro_opens_the_filter()
	{
		var bright = new Macro(0);
		float LevelHigh(float value)
		{
			using var rig = new ChipRig();
			var saw = rig.Chip.AddInstrument(Patch.Build("saw", p =>
			{
				p.Out(p.Wave(Waveform.Saw, 1f, Env.Gate));
				p.Filter(Filter.LowPass(1f));
				p.Macro(bright, MacroTarget.FilterCutoff, 200f, 8000f);
			}));
			rig.Chip.SetMacro(saw, bright, value);
			rig.Chip.NoteOn(saw, 45, 1f);
			rig.Seconds(1.1);
			// The saw's 36th harmonic of 110 Hz, the one nearest 4 kHz
			return AudioAnalysis.ToDb(AudioAnalysis.LevelAt(rig.Left(0.1, 1.0), 3960, ChipRig.Rate));
		}

		(LevelHigh(1f) - LevelHigh(0f)).ShouldBeGreaterThanOrEqualTo(20f);
	}

	private static DrumKit Hats() => DrumKit.FromPatches("hats",
		(Kit.HatClosed, Patch.Build("closed", p => p.Out(p.Noise(Env.Decay(0.04f), 0.5f))), 1),
		(Kit.HatOpen, Patch.Build("open", p => p.Out(p.Noise(Env.Decay(0.35f), 0.5f))), 1));

	[Test]
	public void A_closed_hat_chokes_the_open_one()
	{
		float After(bool closed)
		{
			using var rig = new ChipRig();
			var kit = rig.Chip.AddKit(Hats());
			rig.Chip.Hit(kit, Kit.HatOpen, 1f);
			rig.Seconds(0.1);
			if (closed)
			{
				rig.Chip.Hit(kit, Kit.HatClosed, 1f);
				rig.Session.Render(256);
				var open = rig.Chip.Core.Voices.Single(v => v.Active && v.PieceIndex == 1);
				open.Released.ShouldBeTrue();
			}
			rig.Session.Clear();
			rig.Seconds(0.15);
			return rig.RmsLeft(0.1, 0.05);
		}

		After(closed: true).ShouldBeLessThanOrEqualTo(After(closed: false) / 10f);
	}

	[Test]
	public void A_sampled_kit_plays_its_takes_in_turn_and_its_layers_by_velocity()
	{
		var kit = new DrumKit("sines", new DrumKitPiece(Kit.Snare,
		[
			new DrumLayer(0.5f, [ChipRig.SineSample(500, 0.3)]),
			new DrumLayer(1f, [ChipRig.SineSample(1000, 0.3), ChipRig.SineSample(1500, 0.3)]),
		]));
		using var rig = new ChipRig();
		var drums = rig.Chip.AddKit(kit);

		double HitPitch(float velocity)
		{
			rig.Session.Clear();
			rig.Chip.Hit(drums, Kit.Snare, velocity);
			rig.Seconds(0.4);
			return rig.Pitch(0.02, 0.25);
		}

		HitPitch(1f).ShouldBe(1000, 2);
		HitPitch(1f).ShouldBe(1500, 3);
		HitPitch(1f).ShouldBe(1000, 2);
		HitPitch(0.3f).ShouldBe(500, 2);
	}

	[Test]
	public void A_sample_operator_is_pitched_from_its_root()
	{
		double PitchOf(float note, int dataRate)
		{
			using var rig = new ChipRig();
			var set = new SampleSet(new SampleZone(ChipRig.SineSample(1000, rate: dataRate), RootNote: 69));
			rig.Chip.NoteOn(rig.Chip.AddInstrument(Patch.Build("sampled", p => p.Out(p.Sample(set)))), note, 1f);
			rig.Seconds(0.5);
			return rig.Pitch(0.05, 0.4);
		}

		PitchOf(69, 48_000).ShouldBe(1000, 2);
		PitchOf(81, 48_000).ShouldBe(2000, 4);
		PitchOf(69, 44_100).ShouldBe(1000, 2);
	}

	private static float[] Performance()
	{
		using var rig = new ChipRig();
		var tone = rig.Chip.AddInstrument(Patch.Build("noisy", p =>
		{
			var modulator = p.Sine(2f, Env.Decay(0.3f));
			p.Out(p.Sine(1f, Env.Adsr(0.01f, 0.2f, 0.6f, 0.1f)).ModulatedBy(modulator), p.Noise(Env.Decay(0.1f), 0.2f));
			p.Send(ChipSend.Space, 0.3f);
		}));
		for (var i = 0; i < 6; i++)
		{
			rig.Chip.Play(tone, 50 + 3 * i, 0.7f, 0.1f, pan: -0.5f + 0.2f * i);
			rig.Seconds(0.13);
		}
		rig.Seconds(0.5);
		return rig.Session.Samples.ToArray();
	}

	[Test]
	public void The_same_calls_render_the_same_samples() =>
		Performance().ShouldBe(Performance());

	[Test]
	public void A_chip_of_every_operator_kind_allocates_nothing_once_running()
	{
		var device = new OfflineAudioDevice(ChipRig.Rate, 256);
		using var engine = new AudioEngine(device);
		var chip = new SynthChip();
		var set = new SampleSet(new SampleZone(ChipRig.SineSample(440, 0.2), RootNote: 69));
		var everything = chip.AddInstrument(Patch.Build("everything", p =>
		{
			var modulator = p.Sine(3f, Env.Decay(0.2f));
			var sine = p.Sine(1f, Env.Adsr(0.01f, 0.1f, 0.5f, 0.1f)).ModulatedBy(modulator).Feedback(0.3f);
			var wave = p.Wave(Waveform.Pulse, 1f, Env.Gate, 0.3f, pulseWidth: 0.3f);
			var noise = p.Noise(Env.Decay(0.1f), 0.1f, pink: true);
			var breath = p.Level(Env.Adsr(0.04f, 0.1f, 0.8f, 0.1f));
			var sample = p.Sample(set, 0.5f);
			var pluck = p.String(1f, 1f, 0.3f);
			var flute = p.Tube().DrivenBy(breath, noise);
			p.Out(sine, wave, noise, sample, pluck, flute);
			p.Filter(Filter.LowPass(3000f, envelopeOctaves: 1f, envelope: Env.Decay(0.3f)));
			p.Drive(0.3f);
			p.Vibrato(5f, 0.2f);
			p.Send(ChipSend.Space, 0.2f);
			p.Send(ChipSend.Echo, 0.2f);
		}));
		var kit = chip.AddKit(Hats());
		engine.Play(chip);
		var output = new float[512];

		void Block(int i)
		{
			var note = chip.NoteOn(everything, 48 + i % 24, 0.8f);
			if (i % 3 == 0)
				chip.Hit(kit, i % 2 == 0 ? Kit.HatClosed : Kit.HatOpen, 0.7f);
			device.Render(output);
			chip.NoteOff(note);
			engine.Update();
		}

		for (var i = 0; i < 64; i++)
			Block(i);
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 512; i++)
			Block(i);
		(GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
	}
}
