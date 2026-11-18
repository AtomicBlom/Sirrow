namespace Loom.Audio.Synth.Analysis;

/// <summary>A tone standing out in a sound's spectrum (<see cref="HitMeasures.Tones"/>).</summary>
/// <param name="Hz">Its frequency, between bins.</param>
/// <param name="Db">Its level against the strongest, in decibels (0 for the strongest).</param>
public readonly record struct Tone(float Hz, float Db);
