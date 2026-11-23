namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>One operator phase-modulating a <see cref="OperatorKind.Sine"/>.</summary>
/// <param name="Operator">The modulator's index in the patch: always before the operator it modulates.</param>
/// <param name="Index">How far it swings the phase, in radians at the modulator's full level: higher is brighter.</param>
public readonly record struct Modulation(int Operator, float Index);
