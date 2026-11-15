using Loom.Audio.Synth.Chip;

namespace HelloSynth.Demo;

/// <summary>A drum kit made of patches, so the demo needs no samples: kick, snare, and closed and open hats that choke each other.</summary>
internal static class DemoKit
{
	public static DrumKit Make() => DrumKit.FromPatches("demo kit",
		(Kit.Kick, Patch.Build("kick", p =>
		{
			p.Out(p.Sine(1f, Env.Decay(0.3f)).Fixed(52f));
			p.PitchDrop(24f, 0.04f);
		}), 0),
		(Kit.Snare, Patch.Build("snare", p =>
		{
			p.Out(p.Sine(1f, Env.Decay(0.1f), 0.5f).Fixed(190f), p.Noise(Env.Decay(0.18f), 0.8f));
			p.Filter(Filter.HighPass(250f));
		}), 0),
		(Kit.HatClosed, Patch.Build("closed hat", p =>
		{
			p.Out(p.Noise(Env.Decay(0.05f), 0.45f));
			p.Filter(Filter.HighPass(7000f));
		}), 1),
		(Kit.HatOpen, Patch.Build("open hat", p =>
		{
			p.Out(p.Noise(Env.Decay(0.4f), 0.45f));
			p.Filter(Filter.HighPass(6500f));
		}), 1));
}
