namespace BinaryVibrance.Sirrow.Dsp;

/// <summary>The shape of an <see cref="Envelope"/>: how a note rises, falls to where it holds, and dies away once let go.</summary>
/// <param name="Attack">Seconds from silence to full.</param>
/// <param name="Decay">Seconds for the fall toward <paramref name="Sustain"/> to cover 60 dB.</param>
/// <param name="Sustain">The level held while the note is held, 0 to 1.</param>
/// <param name="Release">Seconds for the fall to silence after the note is let go to cover 60 dB.</param>
public readonly record struct EnvelopeShape(float Attack, float Decay, float Sustain, float Release)
{
	/// <summary>An attack, decay, sustain and release, as given.</summary>
	public static EnvelopeShape Adsr(float attack, float decay, float sustain, float release) => new(attack, decay, sustain, release);

	/// <summary>
	/// A struck sound: at once to full, then dying away over <paramref name="seconds"/> whether held or not. (Named for
	/// what it sounds like, since <see cref="Decay"/> is the shape's own decay time.)
	/// </summary>
	public static EnvelopeShape Struck(float seconds) => new(0.0005f, seconds, 0f, seconds);

	/// <summary>On and off, with only enough of a ramp either way not to click.</summary>
	public static EnvelopeShape Gate => new(0.005f, 0f, 1f, 0.02f);
}
