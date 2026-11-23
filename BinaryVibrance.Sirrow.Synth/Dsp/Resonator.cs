using System.Runtime.CompilerServices;

namespace BinaryVibrance.Sirrow.Synth.Dsp;

/// <summary>
/// One ringing mode, a two-pole resonator: a struck bar, a bell's partial, a body's knock. An impulse of 1 rings at
/// about unit amplitude and dies by 60 dB over its decay.
/// </summary>
public struct Resonator
{
	private float _a1, _a2, _b0;
	private float _y1, _y2;

	/// <summary>Sets the mode's frequency and how long it rings.</summary>
	public void Set(float hz, float decaySeconds, int rate)
	{
		var r = MathF.Exp(-6.9078f / (MathF.Max(decaySeconds, 1e-4f) * rate));
		var w = 2f * MathF.PI * hz / rate;
		_a1 = 2f * r * MathF.Cos(w);
		_a2 = -r * r;
		_b0 = MathF.Sin(w);
	}

	/// <summary>The next sample, struck by <paramref name="x"/>.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Tick(float x)
	{
		var y = _b0 * x + _a1 * _y1 + _a2 * _y2;
		_y2 = _y1;
		_y1 = y;
		return y;
	}

	/// <summary>Sets the mode's memory to 0 where it has decayed below what a float can usefully hold. Call once a block.</summary>
	public void Flush()
	{
		Denormal.Flush(ref _y1);
		Denormal.Flush(ref _y2);
	}
}
