namespace Loom.Audio.Synth.Music;

/// <summary>
/// When a part plays (<see cref="Placement.When"/>) or a section is left by an exit (<see cref="Section.Exit"/>): a cue
/// <see cref="Cue.Above"/> or <see cref="Cue.Below"/> a level, a choice <see cref="Cue{T}.Is"/> a value, or the
/// section's <see cref="Pass"/>, combined with <c>&amp;</c>, <c>|</c> and <c>!</c>.
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

	/// <summary>
	/// Whether it holds for the cues as <paramref name="value"/> gives them, on the first pass of its section: each
	/// comparison as the sequencer makes it (a level at or past its threshold), without its latching, so a tool can say
	/// whether a part's condition is met.
	/// </summary>
	public bool IsMet(Func<CueBase, float> value) => IsMet(value, 1);

	/// <summary>Whether it holds for the cues as <paramref name="value"/> gives them, on pass <paramref name="pass"/> of
	/// its section in a row (<see cref="Pass"/>).</summary>
	public abstract bool IsMet(Func<CueBase, float> value, int pass);

	// Its words, each kind its own
	internal abstract string Describe();

	// A level as the words put it
	internal static string Number(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
