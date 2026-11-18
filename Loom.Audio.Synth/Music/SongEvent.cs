namespace Loom.Audio.Synth.Music;

/// <summary>One note or hit of a compiled placement, in ticks from its section's start.</summary>
internal struct SongEvent
{
	public int Tick;
	public int Length;      // ticks the note sounds; a hit's is its step
	public float Note;      // a MIDI note; 60 for a hit
	public int Piece;       // the kit piece's index, or -1 for a pitched note
	public float Velocity;
}
