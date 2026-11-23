namespace BinaryVibrance.Sirrow.Synth.Analysis;

/// <summary>A stretch of a mix in brief (<see cref="MixMeasures.Spread"/>).</summary>
/// <param name="Rms">Its RMS, linear.</param>
/// <param name="Peak">Its loudest sample, linear.</param>
/// <param name="CentreHz">The spectrum's centre, weighted by power.</param>
/// <param name="Low">The energy's share under 250 Hz, 0 to 1.</param>
/// <param name="Middle">From 250 Hz to 1 kHz.</param>
/// <param name="High">Above 1 kHz.</param>
public readonly record struct Spread(double Rms, double Peak, double CentreHz, double Low, double Middle, double High);
