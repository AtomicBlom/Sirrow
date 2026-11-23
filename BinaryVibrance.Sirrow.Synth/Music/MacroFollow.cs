namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>A macro a track's song steers: from a cue's slot, or held (slot −1, From = To).</summary>
internal readonly record struct MacroFollow(int Macro, int Slot, float From, float To);
