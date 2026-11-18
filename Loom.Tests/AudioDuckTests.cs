using Loom.Audio;

namespace Loom.Tests;

/// <summary>
/// Ducking and the unfocused volume, rendered offline. The trigger's voice is a mono clip panned hard left and the
/// target's a stereo clip, so the right channel is the target alone and shows exactly what the duck did to it.
/// </summary>
public class AudioDuckTests
{
	private const int Rate = 48_000;
	private const int Block = 256;
	private const float Music = 0.5f;

	private static (AudioEngine Engine, OfflineAudioDevice Device, AudioBus Voice, AudioBus Music) Make(AudioOptions? options = null)
	{
		var device = new OfflineAudioDevice(Rate, Block);
		var engine = new AudioEngine(device, options);
		var voice = engine.CreateBus("voice");
		var music = engine.CreateBus("music");
		engine.Play(Constant(engine, Music, Rate, channels: 2), music, loop: true);
		return (engine, device, voice, music);
	}

	private static AudioClip Constant(AudioEngine engine, float value, int frames, int channels = 1)
	{
		var samples = new float[frames * channels];
		Array.Fill(samples, value);
		return engine.CreateClip(samples, channels, Rate);
	}

	// The right channel's last frame after `blocks` more blocks: the target bus alone
	private static float MusicAfter(OfflineAudioDevice device, int blocks = 1)
	{
		var output = new float[Block * 2];
		for (var i = 0; i < blocks; i++)
			device.Render(output);
		return output[^1];
	}

	private static VoiceHandle Speak(AudioEngine engine, AudioBus voice, float level = 0.5f) =>
		engine.Play(Constant(engine, level, Rate), voice, pan: -1f, loop: true);

	private static float Gain(float db) => MathF.Pow(10f, db / 20f);

	[Test]
	public void A_sounding_trigger_turns_the_target_down_to_its_depth()
	{
		var (engine, device, voice, music) = Make();
		engine.AddDuck(voice, music, depthDb: -12f, attackSeconds: 0f);
		MusicAfter(device).ShouldBe(Music, 1e-5f);

		Speak(engine, voice);

		// The level is heard in the first block and acted on from the next, ramped across it
		MusicAfter(device, blocks: 3).ShouldBe(Music * Gain(-12f), 1e-4f);
	}

	[Test]
	public void The_attack_takes_the_target_down_over_its_time()
	{
		var (engine, device, voice, music) = Make();
		engine.AddDuck(voice, music, depthDb: -12f, attackSeconds: 0.1f);
		Speak(engine, voice);

		var early = MusicAfter(device, blocks: 4);    // about a quarter of the way down, in decibels
		var late = MusicAfter(device, blocks: 30);    // well past 0.1 s

		early.ShouldBeLessThan(Music);
		early.ShouldBeGreaterThan(Music * Gain(-12f) * 1.5f);
		late.ShouldBe(Music * Gain(-12f), 1e-4f);
	}

	[Test]
	public void The_target_comes_back_after_the_trigger_stops_and_the_hold_and_release_pass()
	{
		var (engine, device, voice, music) = Make();
		engine.AddDuck(voice, music, depthDb: -12f, attackSeconds: 0f, releaseSeconds: 0.05f, holdSeconds: 0.05f);
		var line = Speak(engine, voice);
		MusicAfter(device, blocks: 4);

		engine.Stop(line);
		var held = MusicAfter(device, blocks: 5);     // ~27 ms: inside the 50 ms hold
		var back = MusicAfter(device, blocks: 40);    // ~213 ms more: hold and release both over

		held.ShouldBe(Music * Gain(-12f), 1e-4f);
		back.ShouldBe(Music, 1e-5f);
	}

	[Test]
	public void A_trigger_below_the_threshold_leaves_the_target_alone()
	{
		var (engine, device, voice, music) = Make();
		engine.AddDuck(voice, music, depthDb: -12f, attackSeconds: 0f, thresholdDb: -40f);

		Speak(engine, voice, level: Gain(-50f));

		MusicAfter(device, blocks: 4).ShouldBe(Music, 1e-5f);
	}

	[Test]
	public void The_trigger_level_is_after_its_bus_volume()
	{
		var (engine, device, voice, music) = Make();
		engine.AddDuck(voice, music, depthDb: -12f, attackSeconds: 0f);
		engine.SetBusVolume(voice, 0f);
		MusicAfter(device); // the volume ramps down across a block, which would be heard with sound on it

		Speak(engine, voice);

		MusicAfter(device, blocks: 4).ShouldBe(Music, 1e-5f);
	}

	[Test]
	public void A_disabled_duck_lets_its_target_back_up_and_enabled_again_ducks_it()
	{
		var (engine, device, voice, music) = Make();
		var duck = engine.AddDuck(voice, music, depthDb: -12f, attackSeconds: 0f, releaseSeconds: 0f);
		Speak(engine, voice);
		MusicAfter(device, blocks: 3).ShouldBe(Music * Gain(-12f), 1e-4f);

		engine.SetDuckEnabled(duck, false);
		MusicAfter(device, blocks: 3).ShouldBe(Music, 1e-5f);
		engine.IsDuckEnabled(duck).ShouldBeFalse();

		engine.SetDuckEnabled(duck, true);
		MusicAfter(device, blocks: 3).ShouldBe(Music * Gain(-12f), 1e-4f);
	}

	[Test]
	public void Two_ducks_on_one_target_take_both_depths()
	{
		var (engine, device, voice, music) = Make();
		var ui = engine.CreateBus("ui");
		engine.AddDuck(voice, music, depthDb: -6f, attackSeconds: 0f);
		engine.AddDuck(ui, music, depthDb: -6f, attackSeconds: 0f);

		Speak(engine, voice);
		Speak(engine, ui);

		MusicAfter(device, blocks: 3).ShouldBe(Music * Gain(-12f), 1e-4f);
	}

	[Test]
	public void A_duck_that_would_turn_down_its_own_trigger_throws()
	{
		var (engine, _, voice, music) = Make();
		var dialogue = engine.CreateBus("dialogue", voice);

		Should.Throw<ArgumentException>(() => engine.AddDuck(voice, voice, -6f));
		Should.Throw<ArgumentException>(() => engine.AddDuck(dialogue, voice, -6f));        // voice holds the trigger
		Should.Throw<ArgumentException>(() => engine.AddDuck(dialogue, engine.Master, -6f)); // so does master
		Should.Throw<ArgumentException>(() => engine.AddDuck(voice, dialogue, -6f));        // the trigger holds the target: it would pump
		Should.Throw<ArgumentOutOfRangeException>(() => engine.AddDuck(voice, music, 0f));
		Should.Throw<ArgumentOutOfRangeException>(() => engine.AddDuck(voice, music, float.NegativeInfinity));
	}

	[Test]
	public void Past_MaxDucks_AddDuck_throws()
	{
		var (engine, _, voice, music) = Make(new AudioOptions { MaxDucks = 1 });
		engine.AddDuck(voice, music, -6f);

		Should.Throw<InvalidOperationException>(() => engine.AddDuck(voice, music, -3f));
	}

	[Test]
	public void Out_of_focus_the_output_fades_to_the_unfocused_volume_and_back()
	{
		var (engine, device, _, _) = Make(new AudioOptions { UnfocusedVolume = 0.25f });

		engine.SetFocused(false);
		var away = MusicAfter(device, blocks: 60); // past the quarter-second fade
		engine.SetFocused(true);
		var back = MusicAfter(device, blocks: 60);

		away.ShouldBe(Music * 0.25f, 1e-5f);
		back.ShouldBe(Music, 1e-5f);
		engine.GetBusVolume(engine.Master).ShouldBe(1f); // the game's own volume is left alone
	}

	[Test]
	public void The_unfocused_volume_set_while_running_is_what_focus_fades_to()
	{
		var (engine, device, _, _) = Make();

		engine.SetUnfocusedVolume(0.25f);
		engine.SetFocused(false);

		MusicAfter(device, blocks: 60).ShouldBe(Music * 0.25f, 1e-5f);
		engine.UnfocusedVolume.ShouldBe(0.25f);
	}

	[Test]
	public void Changing_the_unfocused_volume_in_focus_changes_nothing_heard()
	{
		var (engine, device, _, _) = Make();

		engine.SetUnfocusedVolume(0f);

		MusicAfter(device, blocks: 60).ShouldBe(Music, 1e-5f);
	}

	[Test]
	public void Changing_the_unfocused_volume_out_of_focus_fades_the_output_to_it()
	{
		var (engine, device, _, _) = Make(new AudioOptions { UnfocusedVolume = 0.25f });
		engine.SetFocused(false);
		MusicAfter(device, blocks: 60);

		engine.SetUnfocusedVolume(0.5f);
		MusicAfter(device, blocks: 60).ShouldBe(Music * 0.5f, 1e-5f);

		engine.SetFocused(true);
		MusicAfter(device, blocks: 60).ShouldBe(Music, 1e-5f);
	}

	[Test]
	public void The_unfocused_volume_multiplies_with_the_master_volume()
	{
		var (engine, device, _, _) = Make();
		engine.SetBusVolume(engine.Master, 0.2f);

		engine.SetUnfocusedVolume(0.2f);
		engine.SetFocused(false);

		MusicAfter(device, blocks: 60).ShouldBe(Music * 0.04f, 1e-5f);
		engine.GetBusVolume(engine.Master).ShouldBe(0.2f);
	}

	[Test]
	public void An_unfocused_volume_below_zero_or_not_a_number_throws()
	{
		var (engine, _, _, _) = Make();

		Should.Throw<ArgumentOutOfRangeException>(() => engine.SetUnfocusedVolume(-0.1f));
		Should.Throw<ArgumentOutOfRangeException>(() => engine.SetUnfocusedVolume(float.NaN));
	}
}
