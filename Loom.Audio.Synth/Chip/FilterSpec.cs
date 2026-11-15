using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Chip;

/// <summary>A patch's filter, after its operators: a state-variable filter whose cutoff can follow the note, the velocity and an envelope of its own.</summary>
/// <param name="Kind">Which of its outputs is heard.</param>
/// <param name="CutoffHz">Its cutoff at middle C (note 60), full velocity and the envelope at 0.</param>
/// <param name="Q">Its resonance: 0.7 is flat, higher rings at the cutoff.</param>
/// <param name="KeyTrack">How far the cutoff follows the note: 1 moves it an octave an octave.</param>
/// <param name="Velocity">How far a softer note closes it: 1 is four octaves lower at no velocity.</param>
/// <param name="EnvelopeOctaves">How many octaves its envelope opens it at full level.</param>
/// <param name="Envelope">Its envelope's shape.</param>
public sealed record FilterSpec(FilterKind Kind, float CutoffHz, float Q = 0.7f, float KeyTrack = 0f, float Velocity = 0f,
	float EnvelopeOctaves = 0f, EnvelopeShape Envelope = default);
