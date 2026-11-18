namespace Loom.Audio.Synth.Music;

internal sealed record IsCondition(CueBase Cue, float Value) : Condition
{
	internal override IEnumerable<CueBase> Cues => [Cue];

	internal override string Describe()
	{
		var names = Cue.OptionNames;
		for (var i = 0; i < names.Count; i++)
			if (Cue.OptionValue(i) == Value)
				return $"{Cue.Name} is {names[i]}";
		return $"{Cue.Name} is {Number(Value)}";
	}
}
