namespace BinaryVibrance.Sirrow;

/// <summary>A straight line across a block, from <see cref="AudioParameter.Ramp"/>: call <see cref="Next"/> once a sample.</summary>
/// <param name="start">The value at the block's first sample.</param>
/// <param name="step">How far the value moves each sample.</param>
public struct ParameterRamp(float start, float step)
{
	/// <summary>The value at the current sample.</summary>
	public float Value = start;

	/// <summary>How far the value moves each sample: zero when the parameter isn't changing.</summary>
	public readonly float Step = step;

	/// <summary>Whether the value holds still across the block, so a source can work it out once instead of per sample.</summary>
	public readonly bool IsConstant => Step == 0f;

	/// <summary>The value at the current sample, stepping on to the next.</summary>
	public float Next()
	{
		var value = Value;
		Value += Step;
		return value;
	}
}
