namespace Loom.Audio.Synth.Chip;

/// <summary>
/// One thing for the chip's core to do, from the game thread through the chip's queue, or from a sequencer driving a
/// core directly. A struct, so posting one allocates nothing.
/// </summary>
internal struct ChipEvent
{
	public ChipEventKind Kind;
	public int Instrument;
	public int NoteId;
	public float Note;
	public float Velocity;
	public float Pan;
	public int Frames;          // a note on: frames until it gates off, 0 for none (then Seconds)
	public float Seconds;       // a note on: seconds until it gates off, when Frames is 0; 0 for none
	public int Macro;
	public float Value;
	public int PieceIndex;
	public object? Payload;     // an instrument added: its ChipInstrumentState
	public int GainSlot;        // a note on or hit: 1 + a slot of ChipCore.GainSlots its voice follows; 0, the default, for none
}
