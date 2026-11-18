namespace Loom.Audio.Synth.Chip;

/// <summary>A recording and the notes and velocities it plays for.</summary>
/// <param name="Data">The recording.</param>
/// <param name="RootNote">The note it was recorded at: played there, it sounds as recorded.</param>
/// <param name="LowNote">The lowest note it plays for.</param>
/// <param name="HighNote">The highest note it plays for.</param>
/// <param name="LowVelocity">The softest velocity it plays for, 0 to 1.</param>
/// <param name="HighVelocity">The hardest velocity it plays for, this one included.</param>
public sealed record SampleZone(SampleData Data, int RootNote = 60, int LowNote = 0, int HighNote = 127,
	float LowVelocity = 0f, float HighVelocity = 1f);
