namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>An instrument or kit added to a <see cref="SynthChip"/>, to play.</summary>
/// <param name="Index">Its slot in the chip.</param>
public readonly record struct ChipInstrument(int Index);
