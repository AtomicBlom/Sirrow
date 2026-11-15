namespace Loom.Audio.Synth.Chip;

/// <summary>An operator being built, heard at a level: what <see cref="PatchBuilder.Out"/> takes. An <see cref="Op"/> on its own is heard at 1.</summary>
/// <param name="Op">The operator.</param>
/// <param name="Level">How loud, a linear gain.</param>
public readonly record struct OpTap(Op Op, float Level)
{
	/// <summary>The operator at level 1.</summary>
	public static implicit operator OpTap(Op op) => new(op, 1f);
}
