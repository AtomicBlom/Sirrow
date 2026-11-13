namespace Loom.Audio;

/// <summary>
/// A bus: a group of voices mixed together and set as one, as music, sound effects or UI. Buses form a tree under
/// <see cref="Master"/>, each mixed into its parent; <see cref="AudioEngine.CreateBus"/> makes one. The default value is
/// the master bus.
/// </summary>
/// <param name="Index">The bus's place in its engine; the master bus is 0.</param>
public readonly record struct AudioBus(int Index)
{
	/// <summary>The bus everything ends in: what the device plays.</summary>
	public static AudioBus Master => default;
}
