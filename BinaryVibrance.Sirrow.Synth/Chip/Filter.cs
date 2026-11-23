using BinaryVibrance.Sirrow.Synth.Dsp;

namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>Filters as patches name them: shorthand for <see cref="FilterSpec"/>. Each parameter is <see cref="FilterSpec"/>'s.</summary>
public static class Filter
{
	/// <summary>A low pass: what is below <paramref name="hz"/>.</summary>
	public static FilterSpec LowPass(float hz, float q = 0.7f, float keyTrack = 0f, float velocity = 0f, float envelopeOctaves = 0f, EnvelopeShape? envelope = null) =>
		new(FilterKind.LowPass, hz, q, keyTrack, velocity, envelopeOctaves, envelope ?? default);

	/// <summary>A band pass: what is near <paramref name="hz"/>.</summary>
	public static FilterSpec BandPass(float hz, float q = 0.7f, float keyTrack = 0f, float velocity = 0f, float envelopeOctaves = 0f, EnvelopeShape? envelope = null) =>
		new(FilterKind.BandPass, hz, q, keyTrack, velocity, envelopeOctaves, envelope ?? default);

	/// <summary>A high pass: what is above <paramref name="hz"/>.</summary>
	public static FilterSpec HighPass(float hz, float q = 0.7f, float keyTrack = 0f, float velocity = 0f, float envelopeOctaves = 0f, EnvelopeShape? envelope = null) =>
		new(FilterKind.HighPass, hz, q, keyTrack, velocity, envelopeOctaves, envelope ?? default);
}
