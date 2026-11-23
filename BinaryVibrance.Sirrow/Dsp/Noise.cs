using System.Runtime.CompilerServices;

namespace BinaryVibrance.Sirrow.Dsp;

/// <summary>
/// White noise from a 32-bit xorshift: the same seed gives the same samples on every platform, so a render that uses it
/// can be compared bit for bit.
/// </summary>
/// <param name="seed">Any value; 0, which would give silence, is taken as 0x9E3779B9.</param>
public struct Noise(uint seed)
{
	private uint _x = seed == 0 ? 0x9E3779B9u : seed;

	/// <summary>The next sample, in −1 to 1 (1 itself never comes).</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Next()
	{
		if (_x == 0)
			_x = 0x9E3779B9u;
		_x ^= _x << 13;
		_x ^= _x >> 17;
		_x ^= _x << 5;
		return (int)_x * (1f / 2147483648f);
	}
}
