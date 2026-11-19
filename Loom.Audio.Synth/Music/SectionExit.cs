namespace Loom.Audio.Synth.Music;

/// <summary>
/// One way out of a section (<see cref="Section.Exit"/>, <see cref="Section.Then"/>): to <see cref="To"/>, when
/// <see cref="When"/> holds as the section ends, or always where it is null.
/// </summary>
public sealed record SectionExit(Condition? When, Section To);
