namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>One bar of a section's progression: one chord, or two or four sharing the bar evenly, each as written and
/// as read.</summary>
public sealed record ChordBar(IReadOnlyList<string> Written, IReadOnlyList<Chord> Chords);
