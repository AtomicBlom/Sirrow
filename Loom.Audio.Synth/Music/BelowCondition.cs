namespace Loom.Audio.Synth.Music;

internal sealed record BelowCondition(Cue Cue, float Level) : Condition
{
	internal override IEnumerable<CueBase> Cues => [Cue];

	internal override string Describe() => $"{Cue.Name} below {Number(Level)}";
}
