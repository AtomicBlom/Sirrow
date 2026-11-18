using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Music;

/// <summary>A pitch as a MIDI note number, 69 being A 440; fractions bend it.</summary>
/// <param name="Midi">The MIDI note number.</param>
public readonly record struct Pitch(float Midi)
{
	/// <summary>A note in an octave, as music names them: C4 is middle C, 60.</summary>
	public static Pitch Of(Note note, int octave) => new((octave + 1) * 12 + (int)note);

	/// <summary>Its frequency, in hertz.</summary>
	public float Hz => Units.NoteToHz(Midi);

	/// <summary>The pitch <paramref name="semitones"/> above (or below, negative).</summary>
	public static Pitch operator +(Pitch pitch, int semitones) => new(pitch.Midi + semitones);
}
