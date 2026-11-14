namespace Loom.Audio;

/// <summary>
/// How a 3D voice gets quieter with distance, between its <see cref="AudioSpatial.MinDistance"/> (full volume) and its
/// <see cref="AudioSpatial.MaxDistance"/> (no quieter beyond). The three are OpenAL's clamped models, with
/// <c>d</c> the distance clamped to that range, <c>min</c> and <c>max</c> the two distances and <c>r</c> the
/// <see cref="AudioSpatial.RolloffFactor"/>.
/// </summary>
public enum AudioRolloff
{
	/// <summary><c>min / (min + r (d - min))</c>: halves at twice the minimum distance (r = 1). How sound falls off in the open; the default.</summary>
	Inverse,

	/// <summary><c>1 - r (d - min) / (max - min)</c>: a straight fade to silence at the maximum distance (r = 1). Predictable, and silent at the edge.</summary>
	Linear,

	/// <summary><c>(d / min)^-r</c>: falls faster than inverse for r above 1.</summary>
	Exponential,
}
