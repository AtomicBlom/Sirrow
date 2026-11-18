namespace Loom.Audio.Synth.Chip;

/// <summary>One operator heard in a patch's output.</summary>
/// <param name="Operator">The operator's index in the patch.</param>
/// <param name="Level">How loud, a linear gain.</param>
/// <param name="Pan">Where it is heard, -1 left to 1 right, before the voice's own pan leans it.</param>
public readonly record struct OutputTap(int Operator, float Level, float Pan = 0f);
