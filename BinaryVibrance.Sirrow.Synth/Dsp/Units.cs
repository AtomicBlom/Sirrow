namespace BinaryVibrance.Sirrow.Synth.Dsp;

/// <summary>Conversions between the units sound is written in: MIDI notes and hertz, cents and ratios, decibels and gains.</summary>
public static class Units
{
	/// <summary>The frequency of a MIDI note, 69 being A at 440 Hz; fractions bend between notes.</summary>
	public static float NoteToHz(float note) => 440f * MathF.Pow(2f, (note - 69f) / 12f);

	/// <summary>The MIDI note at a frequency, 69 being A at 440 Hz, with the fraction between notes kept.</summary>
	public static float HzToNote(float hz) => 69f + 12f * MathF.Log2(hz / 440f);

	/// <summary>The frequency ratio of an interval in cents, 1200 to the octave.</summary>
	public static float CentsToRatio(float cents) => MathF.Pow(2f, cents / 1200f);

	/// <summary>The gain a level in decibels stands for: −6.02 dB is a half.</summary>
	public static float DbToGain(float db) => MathF.Pow(10f, db / 20f);

	/// <summary>The level in decibels of a gain, with silence read as −200 dB rather than minus infinity.</summary>
	public static float GainToDb(float gain) => 20f * MathF.Log10(MathF.Max(gain, 1e-10f));
}
