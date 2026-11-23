namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>An operator being built, heard at a level and a place: what <see cref="PatchBuilder.Out"/> takes. An <see cref="Op"/> on its own is heard at 1, in the middle.</summary>
/// <param name="Op">The operator.</param>
/// <param name="Level">How loud, a linear gain.</param>
/// <param name="Pan">Where it is heard, -1 left to 1 right.</param>
public readonly record struct OpTap(Op Op, float Level, float Pan = 0f)
{
	/// <summary>The operator at level 1, in the middle.</summary>
	public static implicit operator OpTap(Op op) => new(op, 1f);
}
