namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>What one of a patch's macros does: as it moves from 0 to 1, its target moves from <paramref name="From"/> to <paramref name="To"/>.</summary>
/// <param name="Macro">The macro.</param>
/// <param name="Target">What it turns.</param>
/// <param name="From">The target's value with the macro at 0 (a multiplier, or a level for drive and sends).</param>
/// <param name="To">Its value with the macro at 1.</param>
/// <param name="Operator">The operator, for <see cref="MacroTarget.OperatorLevel"/>; -1 otherwise.</param>
public sealed record MacroSpec(Macro Macro, MacroTarget Target, float From, float To, int Operator = -1);
