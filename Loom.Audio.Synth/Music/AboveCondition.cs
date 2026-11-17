namespace Loom.Audio.Synth.Music;

internal sealed record AboveCondition(Cue Cue, float Level) : Condition
{
	internal override IEnumerable<CueBase> Cues => [Cue];
}
