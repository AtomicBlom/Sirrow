namespace Loom.Audio.Synth.Music;

/// <summary>A section as the sequencer reads it.</summary>
internal sealed class CompiledSection
{
	public Section Source { get; init; } = null!;
	public string Name { get; init; } = "";
	public int LengthTicks { get; init; }
	public CompiledPlacement[] Placements { get; init; } = [];
	// The macros swept across the section: a track's index, the macro's, and its value at the section's start and end
	public (int Track, int Macro, float From, float To)[] Sweeps { get; init; } = [];
}
