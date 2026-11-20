namespace Loom.Audio.Synth.Music;

/// <summary>A level cue above a level.</summary>
public sealed record AboveCondition(Cue Cue, float Level) : Condition
{
	internal override IEnumerable<CueBase> Cues => [Cue];

	/// <inheritdoc/>
	public override bool IsMet(Func<CueBase, float> value, int pass) => value(Cue) >= Level;

	internal override string Describe() => $"{Cue.Name} above {Number(Level)}";
}
