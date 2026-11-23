using System.Runtime.CompilerServices;

namespace BinaryVibrance.Sirrow.Synth.Dsp;

/// <summary>
/// A delay of up to a fixed number of samples, read at whole or fractional delays: the heart of echoes, choruses,
/// reverbs and plucked strings. Each sample, read first, then <see cref="Write"/>.
/// </summary>
public sealed class DelayLine
{
	private readonly float[] _buffer;
	private readonly int _mask;
	private int _write;

	/// <summary>A line that can delay by up to <paramref name="maxFrames"/> samples, allocated here once.</summary>
	public DelayLine(int maxFrames)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);
		MaxFrames = maxFrames;
		var size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)(maxFrames + 4));
		_buffer = new float[size];
		_mask = size - 1;
	}

	/// <summary>The longest delay it can be read at.</summary>
	public int MaxFrames { get; }

	/// <summary>Stores the next sample.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Write(float x)
	{
		_buffer[_write] = x;
		_write = (_write + 1) & _mask;
	}

	/// <summary>The sample written <paramref name="delay"/> writes ago, 1 being the last, up to <see cref="MaxFrames"/>.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Read(int delay) => _buffer[(_write - delay) & _mask];

	/// <summary>
	/// The line between whole delays, by a four-point Hermite curve through the samples either side: for a delay that
	/// moves (a chorus) or isn't whole (a string's pitch). <paramref name="delay"/> is 2 to <see cref="MaxFrames"/> − 2.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float ReadHermite(float delay)
	{
		var i = (int)delay;
		var t = delay - i;
		var ym1 = Read(i - 1);
		var y0 = Read(i);
		var y1 = Read(i + 1);
		var y2 = Read(i + 2);
		var c1 = 0.5f * (y1 - ym1);
		var c2 = ym1 - 2.5f * y0 + 2f * y1 - 0.5f * y2;
		var c3 = 0.5f * (y2 - ym1) + 1.5f * (y0 - y1);
		return ((c3 * t + c2) * t + c1) * t + y0;
	}

	/// <summary>Silences the line.</summary>
	public void Clear() => Array.Clear(_buffer);
}
