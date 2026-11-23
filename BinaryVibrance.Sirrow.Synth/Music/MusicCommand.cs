namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>One command from the game thread to the sequencer, copied through its queue.</summary>
internal struct MusicCommand
{
	public MusicCommandKind Kind;
	public int A;
	public int B;
	public float Value;
	public object? Payload;
}
