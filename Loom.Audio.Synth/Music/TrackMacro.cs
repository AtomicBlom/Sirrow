using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>One of a track's patch macros, steered by the song: following a cue, or held at a value.</summary>
public sealed class TrackMacro
{
	internal TrackMacro(Macro macro) => Macro = macro;

	internal Macro Macro { get; }
	internal Cue? Cue { get; private set; }
	internal float From { get; private set; }
	internal float To { get; private set; }

	/// <summary>The macro follows <paramref name="cue"/>: at the cue's 0 it is <paramref name="from"/>, at 1 <paramref name="to"/>.</summary>
	public void Follow(Cue cue, float from = 0f, float to = 1f)
	{
		ArgumentNullException.ThrowIfNull(cue);
		Cue = cue;
		From = from;
		To = to;
	}

	/// <summary>The macro held at <paramref name="value"/>, 0 to 1.</summary>
	public void Set(float value)
	{
		Cue = null;
		From = To = value;
	}
}
