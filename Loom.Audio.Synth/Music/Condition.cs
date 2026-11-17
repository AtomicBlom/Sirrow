namespace Loom.Audio.Synth.Music;

/// <summary>
/// When a part plays (<see cref="Placement.When"/>): a cue <see cref="Cue.Above"/> or <see cref="Cue.Below"/> a level,
/// or a choice <see cref="Cue{T}.Is"/> a value, combined with <c>&amp;</c>, <c>|</c> and <c>!</c>.
/// </summary>
public abstract record Condition
{
	private protected Condition() { }

	/// <summary>True while both are.</summary>
	public static Condition operator &(Condition a, Condition b) => new AndCondition(a, b);

	/// <summary>True while either is.</summary>
	public static Condition operator |(Condition a, Condition b) => new OrCondition(a, b);

	/// <summary>True while it isn't.</summary>
	public static Condition operator !(Condition a) => new NotCondition(a);

	// Every cue it reads
	internal abstract IEnumerable<CueBase> Cues { get; }
}
