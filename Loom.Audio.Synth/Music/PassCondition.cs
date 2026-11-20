namespace Loom.Audio.Synth.Music;

/// <summary>
/// Which pass of its section in a row this is, 1 the first time (<see cref="Pass"/> makes it): an exit taken after the
/// second time round, a fill played only on the last pass. Whole numbers compare exactly, so Above and Below are strict.
/// </summary>
public sealed record PassCondition(PassComparison Comparison, int Count) : Condition
{
	internal override IEnumerable<CueBase> Cues => [];

	/// <inheritdoc/>
	public override bool IsMet(Func<CueBase, float> value, int pass) => Comparison switch
	{
		PassComparison.Above => pass > Count,
		PassComparison.Below => pass < Count,
		_ => pass == Count,
	};

	internal override string Describe() => Comparison switch
	{
		PassComparison.Above => $"pass above {Count}",
		PassComparison.Below => $"pass below {Count}",
		_ => $"pass {Count}",
	};
}
