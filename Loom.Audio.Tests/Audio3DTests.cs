using System.Numerics;

using Loom.Audio;
using Loom.Audio.Mixing;

namespace Loom.Tests;

/// <summary>
/// 3D voices: the placement maths on its own (distance, direction, Doppler), then voices placed through the engine,
/// rendered offline. The listener starts at the origin facing -Z with +Y up, so +X is its right.
/// </summary>
public class Audio3DTests
{
	private const int Rate = 48_000;
	private const int Block = 256;
	private static readonly float Centre = MathF.Cos(MathF.PI / 4f); // equal power at the centre: -3 dB a side

	private static (AudioEngine Engine, OfflineAudioDevice Device) Make()
	{
		var device = new OfflineAudioDevice(Rate, Block);
		return (new AudioEngine(device), device);
	}

	private static AudioClip Constant(AudioEngine engine, float value, int frames, int channels = 1, float right = float.NaN)
	{
		var samples = new float[frames * channels];
		for (var i = 0; i < frames; i++)
		{
			samples[i * channels] = value;
			if (channels == 2)
				samples[i * channels + 1] = float.IsNaN(right) ? value : right;
		}
		return engine.CreateClip(samples, channels, Rate);
	}

	private static float[] Render(OfflineAudioDevice device, int blocks = 1)
	{
		var output = new float[Block * 2];
		for (var i = 0; i < blocks; i++)
			device.Render(output);
		return output;
	}

	private static (float Gain, float Pan, float Doppler) Place(Vector3 at, Vector3 velocity = default, Vector3 listenerVelocity = default, AudioSpatial? spatial = null) =>
		Spatializer.Compute(Vector3.Zero, Vector3.UnitX, listenerVelocity, at, velocity, spatial ?? new AudioSpatial(), 343f);

	[Test]
	[Arguments(AudioRolloff.Inverse, 2f, 0.5f)]
	[Arguments(AudioRolloff.Inverse, 4f, 0.25f)]
	[Arguments(AudioRolloff.Linear, 50.5f, 0.5f)]
	[Arguments(AudioRolloff.Exponential, 2f, 0.5f)]
	[Arguments(AudioRolloff.Inverse, 0.5f, 1f)]   // within the minimum distance: full volume
	[Arguments(AudioRolloff.Linear, 500f, 0f)]    // past the maximum: as at the maximum
	public void Rolloff_follows_its_model(AudioRolloff rolloff, float distance, float gain)
	{
		var settings = new AudioSpatial { Rolloff = rolloff };

		Spatializer.Attenuation(distance, settings).ShouldBe(gain, 1e-5f);
	}

	[Test]
	public void A_voice_pans_to_the_side_it_is_on_and_centres_ahead_and_behind()
	{
		Place(new Vector3(5, 0, 0)).Pan.ShouldBe(1f, 1e-5f);
		Place(new Vector3(-5, 0, 0)).Pan.ShouldBe(-1f, 1e-5f);
		Place(new Vector3(0, 0, -5)).Pan.ShouldBe(0f, 1e-5f);
		Place(new Vector3(0, 0, 5)).Pan.ShouldBe(0f, 1e-5f);
		Place(new Vector3(3, 0, -3)).Pan.ShouldBe(MathF.Sqrt(0.5f), 1e-5f);
		Place(Vector3.Zero).ShouldBe((1f, 0f, 1f));
	}

	[Test]
	public void Doppler_raises_an_approaching_voice_and_lowers_a_receding_one()
	{
		var at = new Vector3(10, 0, 0);

		Place(at, velocity: new Vector3(-34.3f, 0, 0)).Doppler.ShouldBe(343f / (343f - 34.3f), 1e-4f);        // coming closer
		Place(at, velocity: new Vector3(34.3f, 0, 0)).Doppler.ShouldBe(343f / (343f + 34.3f), 1e-4f);         // going away
		Place(at, listenerVelocity: new Vector3(34.3f, 0, 0)).Doppler.ShouldBe((343f + 34.3f) / 343f, 1e-4f); // the listener closing in
		Place(at, velocity: new Vector3(0, 0, 50)).Doppler.ShouldBe(1f, 1e-5f);                               // passing across: no shift
		Place(at, velocity: new Vector3(-34.3f, 0, 0), spatial: new AudioSpatial { DopplerFactor = 0f }).Doppler.ShouldBe(1f);
		Place(at, velocity: new Vector3(-10_000f, 0, 0)).Doppler.ShouldBe(4f); // faster than sound: held to two octaves
	}

	[Test]
	public void A_3D_voice_is_placed_from_its_first_block()
	{
		var (engine, device) = Make();
		engine.PlayAt(Constant(engine, 0.5f, Rate), new Vector3(1, 0, 0));

		var output = Render(device);

		// Hard right from the first sample, not swept in from the centre
		output[0].ShouldBe(0f, 1e-5f);
		output[1].ShouldBe(0.5f, 1e-5f);
	}

	[Test]
	public void A_3D_voice_twice_the_minimum_distance_away_plays_at_half_volume()
	{
		var (engine, device) = Make();
		engine.PlayAt(Constant(engine, 0.5f, Rate), new Vector3(0, 0, -2), volume: 1f);

		var output = Render(device);

		output[^2].ShouldBe(0.5f * 0.5f * Centre, 1e-5f);
		output[^1].ShouldBe(0.5f * 0.5f * Centre, 1e-5f);
	}

	[Test]
	public void Moving_a_voice_moves_where_it_is_heard()
	{
		var (engine, device) = Make();
		var voice = engine.PlayAt(Constant(engine, 0.5f, Rate), new Vector3(1, 0, 0));
		Render(device);

		engine.SetPosition(voice, new Vector3(-1, 0, 0));
		Render(device);              // swept across this block
		var output = Render(device);

		output[^2].ShouldBe(0.5f, 1e-5f);
		output[^1].ShouldBe(0f, 1e-5f);
	}

	[Test]
	public void Turning_the_listener_moves_where_a_voice_is_heard()
	{
		var (engine, device) = Make();
		engine.PlayAt(Constant(engine, 0.5f, Rate), new Vector3(1, 0, 0));
		Render(device);

		engine.SetListener(Vector3.Zero, forward: Vector3.UnitX, up: Vector3.UnitY); // now facing the voice
		Render(device);
		var output = Render(device);

		output[^2].ShouldBe(0.5f * Centre, 1e-5f);
		output[^1].ShouldBe(0.5f * Centre, 1e-5f);
	}

	[Test]
	public void A_stereo_clip_in_3D_is_heard_as_one_point()
	{
		var (engine, device) = Make();
		engine.PlayAt(Constant(engine, 1f, Rate, channels: 2, right: 0f), new Vector3(1, 0, 0));

		var output = Render(device);

		output[^2].ShouldBe(0f, 1e-5f);    // the left channel's sound is on the right with the rest
		output[^1].ShouldBe(0.5f, 1e-5f);  // the two channels mixed
	}

	[Test]
	public void An_approaching_voice_plays_faster()
	{
		var (engine, device) = Make();
		// 343 / (343 - 68.6) = 1.25: the 4800-frame clip lasts 3840
		var voice = engine.PlayAt(Constant(engine, 0.5f, 4800), new Vector3(1, 0, 0));
		engine.SetPosition(voice, new Vector3(1, 0, 0), velocity: new Vector3(-68.6f, 0, 0));

		var output = new float[5000 * 2];
		device.Render(output);

		var last = Array.FindLastIndex(output, s => s != 0f) / 2;
		last.ShouldBeInRange(3836, 3844);
	}

	[Test]
	public void Mixing_up_3D_and_plain_voices_throws()
	{
		var (engine, _) = Make();
		var clip = Constant(engine, 0.5f, Rate);
		var placed = engine.PlayAt(clip, new Vector3(1, 0, 0));
		var plain = engine.Play(clip);

		Should.Throw<InvalidOperationException>(() => engine.SetPan(placed, 0.5f));
		Should.Throw<InvalidOperationException>(() => engine.SetPosition(plain, Vector3.Zero));
		Should.Throw<ArgumentOutOfRangeException>(() => engine.PlayAt(clip, Vector3.Zero, spatial: new AudioSpatial { MinDistance = 0f }));
		Should.Throw<ArgumentOutOfRangeException>(() => engine.PlayAt(clip, Vector3.Zero, spatial: new AudioSpatial { MaxDistance = 0.5f }));
		Should.Throw<ArgumentOutOfRangeException>(() => engine.PlayAt(clip, new Vector3(float.NaN, 0, 0)));
		Should.Throw<ArgumentException>(() => engine.SetListener(Vector3.Zero, Vector3.UnitY, Vector3.UnitY));
		Should.Throw<ArgumentException>(() => engine.SetListener(Vector3.Zero, Vector3.Zero, Vector3.UnitY));
	}

	[Test]
	public void Placing_voices_and_the_listener_allocates_nothing_once_running()
	{
		var (engine, device) = Make();
		var clip = Constant(engine, 0.3f, 999, channels: 2);
		var loop = engine.PlayAt(clip, new Vector3(3, 0, -4), loop: true, spatial: new AudioSpatial { Rolloff = AudioRolloff.Linear });
		var shot = Constant(engine, 0.2f, 300);
		var output = new float[Block * 2];

		void Frame(int i)
		{
			var angle = i * 0.05f;
			engine.SetListener(new Vector3(MathF.Sin(angle), 0, 0), new Vector3(MathF.Cos(angle), 0, -1), Vector3.UnitY, new Vector3(1, 0, 0));
			engine.SetPosition(loop, new Vector3(3, 0, -4 + (i % 7)), new Vector3(0, 0, 2));
			engine.PlayAt(shot, new Vector3(-2, 1, i % 5), pitch: 0.9f);
			device.Render(output);
			engine.Update();
		}

		for (var i = 0; i < 64; i++)
			Frame(i);
		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < 512; i++)
				Frame(i);
		});
	}
}
