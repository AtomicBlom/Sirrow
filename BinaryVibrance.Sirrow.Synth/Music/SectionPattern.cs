using BinaryVibrance.Sirrow.Synth.Chip;

namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// What a placement plays through its whole section (<see cref="MusicPlayer.SectionPatternOf"/>): every pass of its part,
/// each sounding over the chords under it, and the fill in the last bar where it has one (from <see cref="FillTick"/>,
/// -1 for none). <see cref="LoopTicks"/> is one pass, whole bars.
/// </summary>
public readonly record struct SectionPattern(int LengthTicks, int LoopTicks, int FillTick, int TicksPerBar, DrumKit? Kit, IReadOnlyList<PatternEvent> Events);
