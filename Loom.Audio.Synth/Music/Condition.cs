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

	/// <summary>The condition in words: "energy above 0.35", "(mood is Calm or energy below 0.2)", "not ...".</summary>
	public sealed override string ToString() => Describe();

	// Its words, each kind its own
	internal abstract string Describe();

	// A level as the words put it
	internal static string Number(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
