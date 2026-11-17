namespace Loom.Audio.Synth.Music;

internal sealed record NotCondition(Condition A) : Condition
{
	internal override IEnumerable<CueBase> Cues => A.Cues;

	internal override string Describe() => $"not {A.Describe()}";
}
