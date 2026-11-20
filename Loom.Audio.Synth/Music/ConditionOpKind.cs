namespace Loom.Audio.Synth.Music;

/// <summary>What one step of a compiled condition does.</summary>
internal enum ConditionOpKind : byte
{
	Above,
	Below,
	Is,
	And,
	Or,
	Not,
	PassAbove,
	PassBelow,
	PassIs,
}
