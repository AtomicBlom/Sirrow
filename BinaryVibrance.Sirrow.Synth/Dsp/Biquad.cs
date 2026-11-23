using System.Runtime.CompilerServices;

namespace BinaryVibrance.Sirrow.Synth.Dsp;

/// <summary>
/// A two-pole, two-zero filter on Robert Bristow-Johnson's cookbook formulas, for equalisation: low and high pass, a
/// peak, and shelves. Set it once, or rarely; a cutoff that moves every block belongs on an <see cref="Svf"/>.
/// </summary>
public struct Biquad
{
	private float _b0, _b1, _b2, _a1, _a2;
	private float _s1, _s2;

	/// <summary>A low pass at <paramref name="hz"/> with resonance <paramref name="q"/> (0.707 is flattest).</summary>
	public void SetLowPass(float hz, float q, int rate)
	{
		var (cs, alpha) = Angle(hz, q, rate);
		Store((1 - cs) / 2, 1 - cs, (1 - cs) / 2, 1 + alpha, -2 * cs, 1 - alpha);
	}

	/// <summary>A high pass at <paramref name="hz"/> with resonance <paramref name="q"/> (0.707 is flattest).</summary>
	public void SetHighPass(float hz, float q, int rate)
	{
		var (cs, alpha) = Angle(hz, q, rate);
		Store((1 + cs) / 2, -(1 + cs), (1 + cs) / 2, 1 + alpha, -2 * cs, 1 - alpha);
	}

	/// <summary>A bell raising or cutting <paramref name="db"/> at <paramref name="hz"/>, as wide as <paramref name="q"/> says.</summary>
	public void SetPeak(float hz, float q, float db, int rate)
	{
		var (cs, alpha) = Angle(hz, q, rate);
		var a = MathF.Pow(10f, db / 40f);
		Store(1 + alpha * a, -2 * cs, 1 - alpha * a, 1 + alpha / a, -2 * cs, 1 - alpha / a);
	}

	/// <summary>A shelf raising or cutting <paramref name="db"/> below <paramref name="hz"/>.</summary>
	public void SetLowShelf(float hz, float db, int rate)
	{
		var (cs, beta, a) = Shelf(hz, db, rate);
		Store(
			a * ((a + 1) - (a - 1) * cs + beta),
			2 * a * ((a - 1) - (a + 1) * cs),
			a * ((a + 1) - (a - 1) * cs - beta),
			(a + 1) + (a - 1) * cs + beta,
			-2 * ((a - 1) + (a + 1) * cs),
			(a + 1) + (a - 1) * cs - beta);
	}

	/// <summary>A shelf raising or cutting <paramref name="db"/> above <paramref name="hz"/>.</summary>
	public void SetHighShelf(float hz, float db, int rate)
	{
		var (cs, beta, a) = Shelf(hz, db, rate);
		Store(
			a * ((a + 1) + (a - 1) * cs + beta),
			-2 * a * ((a - 1) + (a + 1) * cs),
			a * ((a + 1) + (a - 1) * cs - beta),
			(a + 1) - (a - 1) * cs + beta,
			2 * ((a - 1) - (a + 1) * cs),
			(a + 1) - (a - 1) * cs - beta);
	}

	/// <summary>The next filtered sample.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Tick(float x)
	{
		var y = _b0 * x + _s1;
		_s1 = _b1 * x - _a1 * y + _s2;
		_s2 = _b2 * x - _a2 * y;
		return y;
	}

	/// <summary>Sets the filter's memory to 0 where it has decayed below what a float can usefully hold. Call once a block.</summary>
	public void Flush()
	{
		Denormal.Flush(ref _s1);
		Denormal.Flush(ref _s2);
	}

	/// <summary>Forgets what has passed through it, its settings kept: for a filter about to be used again after a rest.</summary>
	public void Reset() => _s1 = _s2 = 0f;

	private static (float Cs, float Alpha) Angle(float hz, float q, int rate)
	{
		var w = 2f * MathF.PI * hz / rate;
		return (MathF.Cos(w), MathF.Sin(w) / (2f * q));
	}

	private static (float Cs, float Beta, float A) Shelf(float hz, float db, int rate)
	{
		var w = 2f * MathF.PI * hz / rate;
		var a = MathF.Pow(10f, db / 40f);
		var alpha = MathF.Sin(w) / 2f * MathF.Sqrt(2f);
		return (MathF.Cos(w), 2f * MathF.Sqrt(a) * alpha, a);
	}

	private void Store(float b0, float b1, float b2, float a0, float a1, float a2)
	{
		_b0 = b0 / a0;
		_b1 = b1 / a0;
		_b2 = b2 / a0;
		_a1 = a1 / a0;
		_a2 = a2 / a0;
	}
}
