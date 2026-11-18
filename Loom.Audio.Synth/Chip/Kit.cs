namespace Loom.Audio.Synth.Chip;

/// <summary>The pieces every kit names the same way, so a song written for one kit plays on another.</summary>
public static class Kit
{
	/// <summary>The bass drum.</summary>
	public static DrumPiece Kick { get; } = new("kick");

	/// <summary>The snare, struck on its head.</summary>
	public static DrumPiece Snare { get; } = new("snare");

	/// <summary>The snare's rim: a cross-stick or rimshot.</summary>
	public static DrumPiece SnareRim { get; } = new("snare-rim");

	/// <summary>The hi-hat, closed: short. Chokes the open one.</summary>
	public static DrumPiece HatClosed { get; } = new("hat-closed");

	/// <summary>The hi-hat, open: rings.</summary>
	public static DrumPiece HatOpen { get; } = new("hat-open");

	/// <summary>The hi-hat closed by the foot.</summary>
	public static DrumPiece HatPedal { get; } = new("hat-pedal");

	/// <summary>The highest tom.</summary>
	public static DrumPiece TomHigh { get; } = new("tom-high");

	/// <summary>The middle tom.</summary>
	public static DrumPiece TomMid { get; } = new("tom-mid");

	/// <summary>The floor tom.</summary>
	public static DrumPiece TomLow { get; } = new("tom-low");

	/// <summary>The crash cymbal.</summary>
	public static DrumPiece Crash { get; } = new("crash");

	/// <summary>The ride cymbal, on its bow.</summary>
	public static DrumPiece Ride { get; } = new("ride");

	/// <summary>The ride's bell.</summary>
	public static DrumPiece RideBell { get; } = new("ride-bell");
}
