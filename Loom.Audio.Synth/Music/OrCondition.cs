namespace Loom.Audio.Synth.Music;

/// <summary>Either of two conditions.</summary>
public sealed record OrCondition(Condition A, Condition B) : Condition
{
	internal override IEnumerable<CueBase> Cues => A.Cues.Concat(B.Cues);

	/// <inheritdoc/>
	public override bool IsMet(Func<CueBase, float> value) => A.IsMet(value) || B.IsMet(value);

	internal override string Describe() => $"({A.Describe()} or {B.Describe()})";
}
