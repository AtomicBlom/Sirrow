namespace Loom.Audio.Synth.Chip;

/// <summary>One piece of a <see cref="DrumKit"/>: sampled (<paramref name="Layers"/>) or synthesised (<paramref name="Patch"/>).</summary>
/// <param name="Piece">Which piece.</param>
/// <param name="Layers">Its recordings by velocity, in rising order; empty for a synthesised piece.</param>
/// <param name="GainDb">Its gain, in decibels.</param>
/// <param name="Pan">Where it sits, -1 left to 1 right.</param>
/// <param name="ChokeGroup">Above 0, a hit silences the other pieces of the same group: a closed hi-hat the open one.</param>
/// <param name="Patch">A synthesised piece's patch, played at note 60; null for a sampled one.</param>
public sealed record DrumKitPiece(DrumPiece Piece, DrumLayer[] Layers, float GainDb = 0f, float Pan = 0f, int ChokeGroup = 0, Patch? Patch = null);
