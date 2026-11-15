namespace Loom.Audio.Synth.Music;

/// <summary>A time signature: <paramref name="Beats"/> notes of 1/<paramref name="Unit"/> a bar.</summary>
/// <param name="Beats">How many of the unit a bar holds: the top number.</param>
/// <param name="Unit">The note that counts: 4 a quarter, 8 an eighth.</param>
public readonly record struct Meter(int Beats, int Unit)
{
	/// <summary>Four quarters a bar.</summary>
	public static Meter FourFour { get; } = new(4, 4);

	/// <summary>Three quarters a bar.</summary>
	public static Meter ThreeFour { get; } = new(3, 4);

	/// <summary>Six eighths a bar.</summary>
	public static Meter SixEight { get; } = new(6, 8);

	/// <summary>A bar's length in ticks, at <see cref="Ticks.PerBeat"/> a quarter.</summary>
	public int TicksPerBar => Beats * Ticks.PerBeat * 4 / Unit;

	/// <inheritdoc/>
	public override string ToString() => $"{Beats}/{Unit}";
}
