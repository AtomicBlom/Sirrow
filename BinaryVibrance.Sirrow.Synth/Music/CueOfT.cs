using System.Runtime.CompilerServices;

namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>A choice the game tells the music, one of an enum's values: which place the player is in, a mood.</summary>
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

	/// <inheritdoc/>
	public override IReadOnlyList<string> OptionNames => [.. Values.Select(v => v.ToString())];

	/// <inheritdoc/>
	public override float OptionValue(int index) => ToValue(Values[index]);

	// The value as the sequencer holds it: the enum's number, read by its size so nothing is boxed (the game sets a choice
	// cue every frame)
	internal static float ToValue(T value) => Unsafe.SizeOf<T>() switch
	{
		1 => Unsafe.As<T, byte>(ref value),
		2 => Unsafe.As<T, short>(ref value),
		4 => Unsafe.As<T, int>(ref value),
		_ => Unsafe.As<T, long>(ref value),
	};
}
