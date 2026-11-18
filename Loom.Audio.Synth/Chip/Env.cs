using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Chip;

/// <summary>Envelopes as patches name them: shorthand for <see cref="EnvelopeShape"/>.</summary>
public static class Env
{
	/// <summary>An attack, decay (seconds to fall 60 dB toward the sustain), sustain (0 to 1) and release.</summary>
	public static EnvelopeShape Adsr(float attack, float decay, float sustain, float release) => EnvelopeShape.Adsr(attack, decay, sustain, release);

	/// <summary>Struck: at once to full, then dying 60 dB over <paramref name="seconds"/> whether held or not.</summary>
	public static EnvelopeShape Decay(float seconds) => EnvelopeShape.Struck(seconds);

	/// <summary>On and off, with only enough of a ramp either way not to click.</summary>
	public static EnvelopeShape Gate => EnvelopeShape.Gate;
}
