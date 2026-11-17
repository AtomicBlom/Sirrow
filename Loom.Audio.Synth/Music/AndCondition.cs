namespace Loom.Audio.Synth.Music;

internal sealed record AndCondition(Condition A, Condition B) : Condition
{
	internal override IEnumerable<CueBase> Cues => A.Cues.Concat(B.Cues);
}
