namespace Loom.Audio.Synth.Chip;

/// <summary>One operator heard in a patch's output.</summary>
/// <param name="Operator">The operator's index in the patch.</param>
/// <param name="Level">How loud, a linear gain.</param>
public readonly record struct OutputTap(int Operator, float Level);
