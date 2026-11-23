namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>A section's way out as the sequencer reads it: its condition's postfix steps (none: always) and the section's
/// index it goes to.</summary>
internal readonly record struct CompiledExit(ConditionOp[] When, int To);
