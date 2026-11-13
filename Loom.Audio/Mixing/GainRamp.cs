namespace Loom.Audio.Mixing;

/// <summary>
/// A gain moving to a target over a number of frames, advanced a block at a time; the mixer interpolates within each
/// block. A change asked for over zero frames still ramps across one block, so no change is ever a step (a click).
/// </summary>
internal struct GainRamp
{
	public float Current;
	public float Target;
	private int _remaining;

	/// <summary>Move to <paramref name="target"/> over <paramref name="frames"/> (0: across the next block).</summary>
	public void Set(float target, int frames)
	{
		Target = target;
		_remaining = frames;
	}

	/// <summary>Be at <paramref name="value"/> now, with no ramp.</summary>
	public void SetNow(float value)
	{
		Current = Target = value;
		_remaining = 0;
	}

	/// <summary>Moves on by <paramref name="frames"/> and returns the gain there: the end of the block's ramp.</summary>
	public float Advance(int frames)
	{
		if (Current == Target || _remaining <= frames)
		{
			Current = Target;
			_remaining = 0;
			return Current;
		}
		Current += (Target - Current) * frames / _remaining;
		_remaining -= frames;
		return Current;
	}
}
