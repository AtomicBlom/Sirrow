namespace Loom.Audio.Synth.Music;

internal sealed record IsCondition(CueBase Cue, float Value) : Condition
{
	internal override IEnumerable<CueBase> Cues => [Cue];
}
