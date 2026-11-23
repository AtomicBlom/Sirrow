using System.Runtime.CompilerServices;

namespace BinaryVibrance.Sirrow.Dsp;

/// <summary>
/// A state-variable filter (Andrew Simper's trapezoidal form), 12 dB an octave, with low, band and high pass outputs
/// from one state. It stays stable and quiet while its cutoff moves, so it can be set every block, unlike a
/// <see cref="Biquad"/>.
/// </summary>
public struct Svf
{
	private float _a1, _a2, _a3, _k;
	private float _ic1, _ic2;

	/// <summary>
	/// Sets the cutoff or centre frequency and the resonance. <paramref name="hz"/> is held between 10 Hz and 0.45 of the
	/// rate, <paramref name="q"/> to at least 0.3; 0.707 is the flattest low pass.
	/// </summary>
	public void Set(float hz, float q, int rate)
	{
		hz = Math.Clamp(hz, 10f, 0.45f * rate);
		q = MathF.Max(q, 0.3f);
		var g = MathF.Tan(MathF.PI * hz / rate);
		_k = 1f / q;
		_a1 = 1f / (1f + g * (g + _k));
		_a2 = g * _a1;
		_a3 = g * _a2;
	}

	/// <summary>
	/// The next sample's three outputs. <paramref name="band"/> is scaled so it passes its centre frequency at unity
	/// whatever the Q.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Tick(float x, out float low, out float band, out float high)
	{
		var v3 = x - _ic2;
		var v1 = _a1 * _ic1 + _a2 * v3;
		var v2 = _ic2 + _a2 * _ic1 + _a3 * v3;
		_ic1 = 2f * v1 - _ic1;
		_ic2 = 2f * v2 - _ic2;
		low = v2;
		band = _k * v1;
		high = x - _k * v1 - v2;
	}

	/// <summary>The next low-passed sample.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float LowPass(float x)
	{
		Tick(x, out var low, out _, out _);
		return low;
	}

	/// <summary>The next band-passed sample, at unity at the centre frequency.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float BandPass(float x)
	{
		Tick(x, out _, out var band, out _);
		return band;
	}

	/// <summary>The next high-passed sample.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float HighPass(float x)
	{
		Tick(x, out _, out _, out var high);
		return high;
	}

	/// <summary>Sets the filter's memory to 0 where it has decayed below what a float can usefully hold. Call once a block.</summary>
	public void Flush()
	{
		Denormal.Flush(ref _ic1);
		Denormal.Flush(ref _ic2);
	}
}
