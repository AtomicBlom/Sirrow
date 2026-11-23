namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>Where a song is: its section, the bar in it and the beat in that, from 1, and the bars since it began, from 0.</summary>
/// <param name="Section">The section's name.</param>
/// <param name="SectionIndex">The section's index in the song.</param>
/// <param name="Bar">The bar of the section, from 1.</param>
/// <param name="Beat">The beat of the bar, from 1.</param>
/// <param name="AbsoluteBar">The bar since the song began, from 0: it keeps counting through sections and swaps.</param>
public readonly record struct MusicPosition(string Section, int SectionIndex, int Bar, int Beat, long AbsoluteBar);
