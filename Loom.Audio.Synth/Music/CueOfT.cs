namespace Loom.Audio.Synth.Music;

/// <summary>A choice the game tells the music, one of an enum's values: which biome the car is in.</summary>
/// <typeparam name="T">The enum.</typeparam>
/// <param name="name">Its name.</param>
/// <param name="initial">Its value before the game sets one.</param>
public sealed class Cue<T>(string name, T initial = default) : CueBase(name) where T : struct, Enum
{
	/// <summary>Its value before the game sets one.</summary>
	public T Initial { get; } = initial;

	internal override float InitialValue => ToValue(Initial);

	/// <summary>True while the cue is <paramref name="value"/>.</summary>
	public Condition Is(T value) => new IsCondition(this, ToValue(value));

	/// <summary>Every value it can take.</summary>
	public IReadOnlyList<T> Values { get; } = Enum.GetValues<T>();

	internal override string[] OptionNames => [.. Values.Select(v => v.ToString())];

	internal override float OptionValue(int index) => ToValue(Values[index]);

	// The value as the sequencer holds it: the enum's number
	internal static float ToValue(T value) => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
}
