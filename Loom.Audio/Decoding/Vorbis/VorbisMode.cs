namespace Loom.Audio.Decoding.Vorbis;

/// <summary>A Vorbis mode: whether its packets are long blocks, and the mapping they decode with.</summary>
/// <param name="LongBlock">The mode's block flag: long blocks (blocksize 1) rather than short.</param>
/// <param name="Mapping">The mapping's index.</param>
internal readonly record struct VorbisMode(bool LongBlock, int Mapping);
