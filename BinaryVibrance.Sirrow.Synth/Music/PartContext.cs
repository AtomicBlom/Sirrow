using BinaryVibrance.Sirrow.Synth.Chip;

namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// What a part needs to become events in one section on one track: the scale, the section's chords, the track's octave and
/// kit, the tempo, and the track's swing.
/// </summary>
internal sealed class PartContext(Scale scale, SectionChords chords, int ticksPerBar, int trackOctave, DrumKit? kit, float tempo,
	float swingRatio = 0.5f, int swingStep = Ticks.Eighth)
{
	// How far an off-beat step moves late: (ratio − 0.5) · 2 · step
	private readonly int _swingShift = (int)Math.Round((swingRatio - 0.5f) * 2f * swingStep);

	public Scale Scale { get; } = scale;
	public int TrackOctave { get; } = trackOctave;
	public DrumKit? Kit { get; } = kit;

	// Milliseconds to ticks at the song's tempo
	public double TicksPerMillisecond { get; } = tempo * Ticks.PerBeat / 60000.0;

	// The chord sounding at a tick of the section
	public Chord ChordAt(int sectionTick) => chords.At(sectionTick, ticksPerBar);

	// A grid step's tick swung: on an odd multiple of the swing's step it moves late, anywhere else it stays. Asked of each
	// step a part places, before anything (a strum's strings) is spread off it
	public int Swung(int sectionTick) =>
		_swingShift != 0 && sectionTick % swingStep == 0 && sectionTick / swingStep % 2 == 1 ? sectionTick + _swingShift : sectionTick;
}
