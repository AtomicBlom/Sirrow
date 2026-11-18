namespace Loom.Audio.Synth.Music;

/// <summary>
/// Something a track plays in a section: a <see cref="Line"/> of notes, a <see cref="Beat"/> of drums, or a
/// <see cref="Strum"/> of chords. Each is written as a string and checked when it is placed (<see cref="Section.Play"/>).
/// </summary>
public abstract class Part
{
	private protected Part() { }

	// The part read against a meter: its length and its notes before the scale, chords and kit resolve them
	internal abstract ParsedPart Parse(Meter meter);

	// Whether the track can play it: a beat needs a kit, the rest a patch
	internal abstract void Check(SongTrack track);
}
