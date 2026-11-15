using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>What a part needs to become notes: the scale, the section's chords, the track's octave or kit, the tempo.</summary>
internal sealed class PartContext(Scale scale, SectionChords chords, int ticksPerBar, int trackOctave, DrumKit? kit, float tempo)
{
	public Scale Scale { get; } = scale;
	public int TrackOctave { get; } = trackOctave;
	public DrumKit? Kit { get; } = kit;

	// Milliseconds to ticks at the song's tempo
	public double TicksPerMillisecond { get; } = tempo * Ticks.PerBeat / 60000.0;

	// The chord sounding at a tick of the section
	public Chord ChordAt(int sectionTick) => chords.At(sectionTick, ticksPerBar);
}
