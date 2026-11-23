namespace BinaryVibrance.Sirrow.Synth.Analysis;

/// <summary>How a partial rings (<see cref="NoteMeasures.Ring"/>). NaN where the note is too short to tell.</summary>
/// <param name="PeakDb">Its loudest, in decibels of full scale.</param>
/// <param name="PeakMs">When it is loudest, in milliseconds from the start.</param>
/// <param name="Early">The seconds it would take to fall 60 dB at the rate it falls over its first 300 ms from the peak; infinite if it doesn't.</param>
/// <param name="Late">The same at the rate it falls after that.</param>
/// <param name="Wobble">How far its level strays from that later fall, in decibels (a standard deviation).</param>
/// <param name="Beat">How often it crosses that fall, as beats a second: two modes a few hertz apart beating.</param>
public readonly record struct PartialRing(float PeakDb, float PeakMs, float Early, float Late, float Wobble, float Beat);
