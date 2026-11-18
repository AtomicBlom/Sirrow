using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>One pass of a placement's part as the sequencer plays it (<see cref="MusicPlayer.Pattern"/>).</summary>
/// <param name="LoopTicks">How long a pass is, in ticks (<see cref="Ticks.PerBeat"/> to a beat).</param>
/// <param name="TicksPerBar">A bar's ticks in the song's meter.</param>
/// <param name="Kit">The kit its track plays, whose pieces <see cref="PatternEvent.Piece"/> indexes; null for a patch.</param>
/// <param name="Events">Its notes or hits, in the order they sound.</param>
public readonly record struct PlacementPattern(int LoopTicks, int TicksPerBar, DrumKit? Kit, IReadOnlyList<PatternEvent> Events);
