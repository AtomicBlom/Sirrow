namespace Loom.Audio.Synth.Chip;

/// <summary>What a <see cref="ChipEvent"/> asks the chip's core to do.</summary>
internal enum ChipEventKind : byte
{
	AddInstrument,
	NoteOn,
	NoteOff,
	Hit,
	Macro,
	AllOff,
	ReplaceInstrument,
}
