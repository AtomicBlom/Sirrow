namespace Loom.Audio.Synth.Chip;

/// <summary>
/// One of an instrument's eight macros, 0 to 7: a control a game turns from 0 to 1 (<see cref="SynthChip.SetMacro"/>)
/// that moves whatever the patch's <see cref="MacroSpec"/>s for it say. A game names its own.
/// </summary>
/// <param name="Id">0 to 7.</param>
public readonly record struct Macro(int Id)
{
	/// <summary>How many macros an instrument has.</summary>
	public const int Count = 8;
}
