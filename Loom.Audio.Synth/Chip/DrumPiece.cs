namespace Loom.Audio.Synth.Chip;

/// <summary>A piece of a drum kit, by the name kits give it: <see cref="Kit"/> has the common ones.</summary>
/// <param name="Id">Its name, in lower-kebab-case: "kick", "hat-closed".</param>
public readonly record struct DrumPiece(string Id);
