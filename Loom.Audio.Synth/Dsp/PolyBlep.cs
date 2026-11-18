using System.Runtime.CompilerServices;

namespace Loom.Audio.Synth.Dsp;

/// <summary>
/// Band-limited sawtooth and pulse waves by polynomial band-limited steps: each jump in the naive wave is smoothed over
/// the samples either side of it, which removes most of the aliasing a naive wave folds back below the rate.
/// </summary>
public static class PolyBlep
{
	/// <summary>The correction to add at a rising jump at phase 0, for <paramref name="t"/> the phase since it.</summary>
	/// <param name="t">The phase, 0 to 1.</param>
	/// <param name="dt">The phase step per sample: frequency / rate.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Residual(float t, float dt)
	{
		if (t < dt)
		{
			t /= dt;
			return t + t - t * t - 1f;
		}
		if (t > 1f - dt)
		{
			t = (t - 1f) / dt;
			return t * t + t + t + 1f;
		}
		return 0f;
	}

	/// <summary>A sawtooth rising from −1 to 1 over each cycle.</summary>
	/// <param name="phase">The phase, 0 to 1.</param>
	/// <param name="dt">The phase step per sample: frequency / rate.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Saw(float phase, float dt) => 2f * phase - 1f - Residual(phase, dt);

	/// <summary>A pulse, 1 for the first <paramref name="width"/> of each cycle and −1 after; 0.5 is a square.</summary>
	/// <param name="phase">The phase, 0 to 1.</param>
	/// <param name="dt">The phase step per sample: frequency / rate.</param>
	/// <param name="width">The share of the cycle spent high, 0 to 1.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Square(float phase, float dt, float width)
	{
		var fall = phase + 1f - width;
		fall -= MathF.Floor(fall);
		return (phase < width ? 1f : -1f) + Residual(phase, dt) - Residual(fall, dt);
	}
}
