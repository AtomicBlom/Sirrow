namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>A note or a hit of a <see cref="PlacementPattern"/>.</summary>
/// <param name="Tick">When it starts, from the pass's start.</param>
/// <param name="Length">How long it sounds, in ticks.</param>
/// <param name="Note">Its MIDI note (60 for a hit).</param>
/// <param name="Piece">The kit piece it hits, by its index in <see cref="Chip.DrumKit.Pieces"/>; -1 for a note.</param>
/// <param name="Velocity">How hard, 0 to 1.</param>
public readonly record struct PatternEvent(int Tick, int Length, float Note, int Piece, float Velocity);
