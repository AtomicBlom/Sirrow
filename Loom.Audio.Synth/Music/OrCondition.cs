namespace Loom.Audio.Synth.Music;

internal sealed record OrCondition(Condition A, Condition B) : Condition
{
	internal override IEnumerable<CueBase> Cues => A.Cues.Concat(B.Cues);

	internal override string Describe() => $"({A.Describe()} or {B.Describe()})";
}
