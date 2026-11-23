using BinaryVibrance.Sirrow.Synth.Chip;

namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>A track as the sequencer reads it.</summary>
internal sealed class CompiledTrack
{
	public string Name { get; init; } = "";
	public Patch? Patch { get; init; }
	public DrumKit? Kit { get; init; }
	public int Group { get; init; }
	public float Gain { get; init; } = 1f;
	public float Pan { get; init; }
	public float HumaniseMs { get; init; }
	public float HumaniseVelocity { get; init; }
	public uint Seed { get; init; }
	public MacroFollow[] Macros { get; init; } = [];
}
