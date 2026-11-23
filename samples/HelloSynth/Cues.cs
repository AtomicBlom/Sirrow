using BinaryVibrance.Sirrow.Synth.Music;

namespace HelloSynth;

/// <summary>What the music listens to. A game keeps its cues like these and sets them as it plays; the workbench sets them from its sliders.</summary>
public static class Cues
{
	/// <summary>How much is going on, 0 to 1: the lead comes in above a half, and its filter opens with it.</summary>
	public static Cue Intensity { get; } = new("intensity", 0.3f);

	/// <summary>Which motif plays.</summary>
	public static Cue<HelloSynth.Mood> Mood { get; } = new("mood");
}
