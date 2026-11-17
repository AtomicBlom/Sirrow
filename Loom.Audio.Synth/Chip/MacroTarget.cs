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
	/// <summary>
	/// Moves the filter's cutoff by octaves: from <see cref="MacroSpec.From"/> octaves at the macro's 0 to
	/// <see cref="MacroSpec.To"/> at its 1. A sweep by this is even to the ear all the way; one by
	/// <see cref="FilterCutoff"/>, which moves the cutoff in hertz, does most of what it does in its first few steps.
	/// </summary>
	FilterOctaves,
}
