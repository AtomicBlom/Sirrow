using System.Runtime.CompilerServices;

namespace Loom.Audio.Synth.Dsp;

/// <summary>A one-pole filter, 6 dB an octave: the gentlest low or high pass, cheap enough to set every block.</summary>
public struct OnePole
{
	private float _a;
	private float _z;

	/// <summary>Sets the corner frequency, where the low pass is 3 dB down.</summary>
	public void Set(float hz, int rate) => _a = 1f - MathF.Exp(-2f * MathF.PI * hz / rate);

	/// <summary>The next low-passed sample.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float LowPass(float x) => _z += _a * (x - _z);

	/// <summary>The next high-passed sample: what the low pass takes out. Use one filter for one or the other, not both.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float HighPass(float x) => x - LowPass(x);

	/// <summary>Sets the filter's memory to 0 where it has decayed below what a float can usefully hold.</summary>
	public void Flush() => Denormal.Flush(ref _z);
}
