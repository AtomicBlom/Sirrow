namespace Loom.Audio.Synth.Analysis;

/// <summary>A partial of a note (<see cref="NoteMeasures.Partials"/>).</summary>
/// <param name="Hz">Its frequency, between bins; NaN for a harmonic that isn't there.</param>
/// <param name="Db">Its level against the strongest, in decibels.</param>
/// <param name="Harmonic">Which harmonic it is, from 1; 0 for a note whose partials aren't harmonics.</param>
public readonly record struct Partial(float Hz, float Db, int Harmonic);
