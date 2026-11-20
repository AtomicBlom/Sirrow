namespace Loom.Audio.Synth.Music;

/// <summary>Both of two conditions.</summary>
public sealed record AndCondition(Condition A, Condition B) : Condition
{
	internal override IEnumerable<CueBase> Cues => A.Cues.Concat(B.Cues);

	/// <inheritdoc/>
	public override bool IsMet(Func<CueBase, float> value, int pass) => A.IsMet(value, pass) && B.IsMet(value, pass);

	internal override string Describe() => $"{A.Describe()} and {B.Describe()}";
}
