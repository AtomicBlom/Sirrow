namespace BinaryVibrance.Sirrow.Synth.Analysis;

/// <summary>A mix's balance (<see cref="MixMeasures.Balance"/>).</summary>
/// <param name="Shares">Each band's share of the energy, 0 to 1, lowest band first.</param>
/// <param name="CentreLow">The spectrum's centre, in hertz, at the lower quartile of the blocks that sound.</param>
/// <param name="CentreMedian">The same at the median.</param>
/// <param name="CentreHigh">The same at the upper quartile.</param>
/// <param name="CrestDb">The median of each second's peak over its RMS, in decibels: how far the peaks stand over the average. NaN under a second.</param>
/// <param name="Likeness">How alike the two sides are, their correlation: 1 is mono.</param>
public sealed record Balance(double[] Shares, double CentreLow, double CentreMedian, double CentreHigh, double CrestDb, double Likeness);
