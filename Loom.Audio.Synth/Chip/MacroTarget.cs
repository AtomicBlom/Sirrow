namespace Loom.Audio.Synth.Chip;

/// <summary>What a patch's <see cref="MacroSpec"/> turns as its macro moves from 0 to 1.</summary>
public enum MacroTarget
{
	/// <summary>Multiplies the filter's cutoff.</summary>
	FilterCutoff,
	/// <summary>Sets the drive, 0 to 1.</summary>
	Drive,
	/// <summary>Multiplies the voice's gain.</summary>
	Gain,
	/// <summary>Sets the send to the space (the room).</summary>
	SendSpace,
	/// <summary>Sets the send to the echo.</summary>
	SendEcho,
	/// <summary>Multiplies the vibrato's depth.</summary>
	VibratoDepth,
	/// <summary>Multiplies one operator's level (<see cref="MacroSpec.Operator"/>).</summary>
	OperatorLevel,
}
