namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// A level the game tells the music, usually 0 to 1: how much is going on, how near the end is. Parts come and go on it
/// (<see cref="Above"/>, <see cref="Below"/>), a part's volume follows it (<see cref="Placement.Level"/>), and a patch's
/// macro can follow it (<see cref="TrackMacro.Follow"/>).
/// </summary>
/// <param name="name">Its name.</param>
/// <param name="initial">Its value before the game sets one.</param>
public sealed class Cue(string name, float initial = 0f) : CueBase(name)
{
	/// <summary>Its value before the game sets one.</summary>
	public float Initial { get; } = initial;

	internal override float InitialValue => Initial;

	/// <summary>True at <paramref name="level"/> or more; false again only below <paramref name="level"/> − 0.05, so a part doesn't flap while the cue hovers.</summary>
	public Condition Above(float level) => new AboveCondition(this, level);

	/// <summary>True at <paramref name="level"/> or less; false again only above <paramref name="level"/> + 0.05.</summary>
	public Condition Below(float level) => new BelowCondition(this, level);
}
