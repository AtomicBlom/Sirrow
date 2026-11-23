namespace BinaryVibrance.Sirrow.Synth.Effects;

/// <summary>
/// An effect that listens to a sound other than the one it changes: a compressor that turns the bass down while the kick
/// sounds. On a <see cref="Chip.SynthChip"/>'s group, its sends or its master, it hears the chip's key
/// (<see cref="Chip.ChipSend.Key"/>), what its patches send there.
/// </summary>
/// <remarks>
/// Whoever runs the effect hands it the key before each block, every block, even while the effect rests
/// (<see cref="AudioEffect.IsResting"/>) with nothing coming in, so what it follows of the key is never stale. Audio
/// thread: allocate nothing.
/// </remarks>
public interface IKeyedEffect
{
	/// <summary>The key for the block about to be run: two spans of the block's length, only read.</summary>
	void Key(ReadOnlySpan<float> left, ReadOnlySpan<float> right);
}
