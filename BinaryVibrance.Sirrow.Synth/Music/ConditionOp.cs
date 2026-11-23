namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>One step of a condition compiled to postfix, run on a fixed stack by the sequencer.</summary>
internal struct ConditionOp
{
	public ConditionOpKind Kind;
	public int Slot;     // the cue's slot, for Above, Below and Is
	public float Value;  // the level, the choice's number, or the pass count
	public int Latch;    // Above and Below: its latch state's index, for the 0.05 margin
}
