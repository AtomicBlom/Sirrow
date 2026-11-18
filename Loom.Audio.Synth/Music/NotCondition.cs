namespace Loom.Audio.Synth.Music;

/// <summary>A condition not holding.</summary>
public sealed record NotCondition(Condition A) : Condition
{
	internal override IEnumerable<CueBase> Cues => A.Cues;

	/// <inheritdoc/>
	public override bool IsMet(Func<CueBase, float> value) => !A.IsMet(value);

	internal override string Describe() => $"not {A.Describe()}";
}
