namespace Loom.Audio.Synth.Music;

/// <summary>Music's clock: 960 ticks a quarter note (a beat), so eighths, sixteenths and triplets are all whole numbers.</summary>
public static class Ticks
{
	/// <summary>Ticks a beat, a quarter note.</summary>
	public const int PerBeat = 960;

	/// <summary>A quarter note.</summary>
	public const int Quarter = 960;

	/// <summary>An eighth note.</summary>
	public const int Eighth = 480;

	/// <summary>A sixteenth note.</summary>
	public const int Sixteenth = 240;

	/// <summary>An eighth-note triplet: three to a quarter.</summary>
	public const int Triplet = 320;

	// The name of a step, for messages: "eighths"
	internal static string Name(int step) => step switch
	{
		Quarter => "quarters",
		Eighth => "eighths",
		Sixteenth => "sixteenths",
		Triplet => "triplets",
		_ => $"steps of {step} ticks",
	};
}
