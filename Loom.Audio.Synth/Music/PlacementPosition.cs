namespace Loom.Audio.Synth.Music;

/// <summary>
/// Where a placement's part is in the section playing (<see cref="MusicPlayer.PositionOf"/>): which pass of the part, from
/// 0, of how many fit before the section's end (or before its fill, which takes the last bar), the tick within that pass,
/// and whether the fill is playing. <see cref="IsPlaying"/> is false while another section plays.
/// </summary>
public readonly record struct PlacementPosition(bool IsPlaying, int Pass, int Passes, int Tick, int LoopTicks, bool InFill);
