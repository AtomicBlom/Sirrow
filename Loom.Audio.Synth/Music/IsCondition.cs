namespace Loom.Audio.Synth.Music;

/// <summary>A choice cue at one of its options, or any cue at one value.</summary>
public sealed record IsCondition(CueBase Cue, float Value) : Condition
{
	internal override IEnumerable<CueBase> Cues => [Cue];

	/// <inheritdoc/>
	public override bool IsMet(Func<CueBase, float> value, int pass) => value(Cue) == Value;

	internal override string Describe()
	{
		var names = Cue.OptionNames;
		for (var i = 0; i < names.Count; i++)
			if (Cue.OptionValue(i) == Value)
				return $"{Cue.Name} is {names[i]}";
		return $"{Cue.Name} is {Number(Value)}";
	}
}
