using System.Runtime.CompilerServices;

namespace BinaryVibrance.Sirrow.Dsp;

/// <summary>
/// Turns white noise pink, 3 dB an octave quieter going up, so each octave carries the same energy: three one-pole
/// filters summed (Paul Kellet's economy filter), good to about ±0.5 dB across the audible range at 44.1 to 48 kHz.
/// </summary>
public struct PinkFilter
{
	private float _b0, _b1, _b2;

	/// <summary>The next pink sample from the next white one.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Tick(float white)
	{
		_b0 = 0.99765f * _b0 + white * 0.0990460f;
		_b1 = 0.96300f * _b1 + white * 0.2965164f;
		_b2 = 0.57000f * _b2 + white * 1.0526913f;
		return (_b0 + _b1 + _b2 + white * 0.1848f) * 0.2f;
	}
}
