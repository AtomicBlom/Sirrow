namespace Loom.Audio.Synth.Music;

/// <summary>A part read against the song's meter: how long one pass of it is, and how it becomes events.</summary>
internal abstract class ParsedPart(int lengthTicks)
{
	// One pass, in whole bars
	public int LengthTicks { get; } = lengthTicks;

	// One pass's events, placed `offset` ticks into the section; nothing at or after `limit` (the section's end, or a fill's
	// bar), and nothing sounding past it
	public abstract void Emit(PartContext context, int offset, int limit, List<SongEvent> into);
}
