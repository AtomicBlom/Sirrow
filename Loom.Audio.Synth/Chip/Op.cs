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

	/// <summary>Excited by <paramref name="drivers"/>, each made before this one: a <see cref="OperatorKind.String"/>, a <see cref="OperatorKind.Tube"/> or <see cref="OperatorKind.Modes"/> only.</summary>
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

	/// <summary>
	/// Starting each note <paramref name="cycles"/> of the way through its cycle (0 to 1): a sine or a wave. Give several
	/// detuned waves different starts and they are apart from the first sample, where all from 0 they start as one.
	/// </summary>
	public Op Phase(float cycles)
	{
		Spec = Spec with { StartPhase = cycles - MathF.Floor(cycles) };
		return this;
	}

	/// <summary>At <paramref name="hz"/> whatever the note, though the patch's pitch drop still bends it.</summary>
	public Op Fixed(float hz)
	{
		Spec = Spec with { FixedHz = hz };
		return this;
	}

	/// <summary>Louder or quieter by <paramref name="dbPerOctave"/> for each octave above middle C, and the other way below it: negative keeps high notes from growing harsh.</summary>
	public Op KeyLevel(float dbPerOctave)
	{
		Spec = Spec with { KeyLevelDb = dbPerOctave };
		return this;
	}

	/// <summary>
	/// Ringing shorter the higher the note, as struck and plucked things do: each octave above middle C shortens its decay
	/// and release (a string's ring, a body's modes) by <paramref name="share"/> of themselves, and each below lengthens them.
	/// </summary>
	public Op KeyDecay(float share = 0.5f)
	{
		Spec = Spec with { KeyDecay = share };
		return this;
	}

	/// <summary>
	/// This operator heard at <paramref name="level"/> and at <paramref name="pan"/> (-1 left to 1 right), for
	/// <see cref="PatchBuilder.Out"/>. Operators heard at different places make a patch wide: two saws a few cents apart,
	/// one each side, are a section and not a single player.
	/// </summary>
	public OpTap At(float level, float pan = 0f) => new(this, level, pan);
}
