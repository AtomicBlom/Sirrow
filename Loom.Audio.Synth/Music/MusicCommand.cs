namespace Loom.Audio.Synth.Music;

/// <summary>What a <see cref="MusicCommand"/> asks the sequencer to do.</summary>
internal enum MusicCommandKind : byte
{
	Swap,       // Payload: the new SongState; taken at the next bar line
	Next,       // A: the section to play after this one
	Jump,       // A: the section to go to at the next bar line
	Loop,       // Value: 1 holds the current section, 0 lets it go on
	Mute,       // A: the track; Value: 1 muted
	Solo,       // A: the track; Value: 1 soloed
	TrackGain,  // A: the track; Value: a linear gain over its level
	Force,      // A: the section; B: the placement; Value: the PlacementState
}

/// <summary>One command from the game thread to the sequencer, copied through its queue.</summary>
internal struct MusicCommand
{
	public MusicCommandKind Kind;
	public int A;
	public int B;
	public float Value;
	public object? Payload;
}
