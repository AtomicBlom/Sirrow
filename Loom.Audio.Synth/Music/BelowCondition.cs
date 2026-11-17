namespace Loom.Audio.Synth.Music;

/// <summary>A level cue below a level.</summary>
public sealed record BelowCondition(Cue Cue, float Level) : Condition
{
	internal override IEnumerable<CueBase> Cues => [Cue];

	/// <inheritdoc/>
	public override bool IsMet(Func<CueBase, float> value) => value(Cue) < Level;

	internal override string Describe() => $"{Cue.Name} below {Number(Level)}";
}
