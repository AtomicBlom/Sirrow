namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>A note started with <see cref="SynthChip.NoteOn"/>, to let go with <see cref="SynthChip.NoteOff"/>.</summary>
/// <param name="Id">Its number, from 1; 0 is no note.</param>
public readonly record struct NoteHandle(int Id);
