namespace Loom.Audio.Synth.Chip;

/// <summary>
/// A group of a chip's instruments sharing a chain of effects, before the master: guitars through a distortion, drums
/// through a compressor. The effects are factories, since each chip makes its own.
/// </summary>
/// <param name="Name">Its name, for lists.</param>
/// <param name="Effects">Its effects, in order.</param>
public sealed record ChipGroup(string Name, params Func<AudioEffect>[] Effects);
