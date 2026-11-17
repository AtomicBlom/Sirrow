namespace Loom.Audio.Synth.Music;

internal sealed record NotCondition(Condition A) : Condition
{
	internal override IEnumerable<CueBase> Cues => A.Cues;
}
