namespace Loom.Audio.Synth.Chip;

/// <summary>One operator being built by a <see cref="PatchBuilder"/>: its methods set it up and return it, so they chain.</summary>
public sealed class Op
{
	internal Op(PatchBuilder owner, int index, OperatorSpec spec)
	{
		Owner = owner;
		Index = index;
		Spec = spec;
	}

	internal PatchBuilder Owner { get; }
	internal int Index { get; }
	internal OperatorSpec Spec { get; set; }
	internal List<(Op Op, float Index)> Modulators { get; } = [];
	internal List<Op> Drivers { get; } = [];

	/// <summary>Phase-modulated by <paramref name="modulator"/>, made before this one, swinging the phase <paramref name="index"/> radians at its full level.</summary>
	public Op ModulatedBy(Op modulator, float index = 2f)
	{
		ArgumentNullException.ThrowIfNull(modulator);
		Modulators.Add((modulator, index));
		return this;
	}

	/// <summary>Fed back into its own phase by <paramref name="radians"/>: a <see cref="OperatorKind.Sine"/> only.</summary>
	public Op Feedback(float radians)
	{
		Spec = Spec with { Feedback = radians };
		return this;
	}

	/// <summary>Excited by <paramref name="drivers"/>, each made before this one: a <see cref="OperatorKind.String"/> or <see cref="OperatorKind.Tube"/> only.</summary>
	public Op DrivenBy(params Op[] drivers)
	{
		ArgumentNullException.ThrowIfNull(drivers);
		Drivers.AddRange(drivers);
		return this;
	}

	/// <summary>Off its pitch by <paramref name="cents"/>.</summary>
	public Op Detune(float cents)
	{
		Spec = Spec with { DetuneCents = cents };
		return this;
	}

	/// <summary>At <paramref name="hz"/> whatever the note.</summary>
	public Op Fixed(float hz)
	{
		Spec = Spec with { FixedHz = hz };
		return this;
	}

	/// <summary>This operator heard at <paramref name="level"/>, for <see cref="PatchBuilder.Out"/>.</summary>
	public OpTap At(float level) => new(this, level);
}
