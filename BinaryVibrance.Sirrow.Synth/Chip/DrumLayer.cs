namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>A drum piece's recordings for hits up to a velocity, with takes played in turn.</summary>
/// <param name="UpToVelocity">The hardest velocity this layer plays for, 0 to 1.</param>
/// <param name="Takes">Recordings of the same hit, alternated so repeats don't sound mechanical.</param>
public sealed record DrumLayer(float UpToVelocity, SampleData[] Takes);
