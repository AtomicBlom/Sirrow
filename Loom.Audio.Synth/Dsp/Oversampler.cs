namespace Loom.Audio.Synth.Dsp;

/// <summary>
/// Raises a block to 2 or 4 times the rate and brings it back down, so a waveshaper run between the two (distortion,
/// saturation) doesn't fold its new harmonics back below the rate as aliasing. Each 2× step is a 63-tap half-band
/// filter (a Blackman-windowed sinc); a factor of 4 is two steps each way. One per channel; it allocates only here.
/// </summary>
/// <remarks>
/// A half-band filter's odd taps are zero but for the centre, and a signal raised by putting a zero between each pair of
/// samples has half its samples zero, so each step works only with what isn't: 32 taps and a centre tap for the samples
/// it computes. The terms are summed in the order the full filter sums them.
/// </remarks>
public sealed class Oversampler
{
	private const int Taps = 63;
	private const int Middle = (Taps - 1) / 2;
	private const int EvenTaps = Middle + 1;
	private static readonly float[] HalfBand = MakeHalfBand();
	private static readonly float Centre = HalfBand[Middle];

	// The even taps, last first: Reversed[m] = h[2 (31 − m)], so a window of samples oldest first lines up with it
	private static readonly float[] Reversed = MakeReversed();

	private readonly Stage _up1, _up2, _down1, _down2;
	private readonly float[] _scratch;

	/// <summary>An oversampler by <paramref name="factor"/>, 2 or 4, for blocks of up to <paramref name="maxBlockFrames"/> frames.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A factor other than 2 or 4, or a block size under 1.</exception>
	public Oversampler(int factor, int maxBlockFrames = 256)
	{
		if (factor is not (2 or 4))
			throw new ArgumentOutOfRangeException(nameof(factor), factor, "An oversampler raises the rate by 2 or 4.");
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBlockFrames);
		Factor = factor;
		MaxBlockFrames = maxBlockFrames;
		_up1 = new Stage();
		_up2 = new Stage();
		_down1 = new Stage();
		_down2 = new Stage();
		_scratch = new float[maxBlockFrames * 2];
	}

	/// <summary>How many times the rate it raises a block to.</summary>
	public int Factor { get; }

	/// <summary>The longest block, at the base rate, it takes.</summary>
	public int MaxBlockFrames { get; }

	/// <summary>
	/// How far a block taken up and back down is delayed, in frames at the base rate: 31 at 2×, 47 at 4×, a whole number so
	/// a dry signal delayed by it lines up exactly (a mix of the two then doesn't comb).
	/// </summary>
	public int LatencyFrames => Factor == 2 ? 31 : 47;

	private float _carry; // the last sample at twice the rate from the 4× path's previous block, for its one-sample delay

	/// <summary>Raises <paramref name="input"/> to <see cref="Factor"/> times its rate in <paramref name="output"/>, which holds input.Length · Factor samples.</summary>
	public void Up(ReadOnlySpan<float> input, Span<float> output)
	{
		Check(input.Length, output.Length);
		if (Factor == 2)
		{
			_up1.Up(input, output);
			return;
		}
		var middle = _scratch.AsSpan(0, input.Length * 2);
		_up1.Up(input, middle);
		_up2.Up(middle, output);
	}

	/// <summary>Brings <paramref name="input"/>, at <see cref="Factor"/> times the rate, back down into <paramref name="output"/>, which holds input.Length / Factor samples.</summary>
	public void Down(ReadOnlySpan<float> input, Span<float> output)
	{
		Check(output.Length, input.Length);
		if (Factor == 2)
		{
			_down1.Down(input, output);
			return;
		}
		var middle = _scratch.AsSpan(0, output.Length * 2);
		_down2.Down(input, middle);
		// One sample more at twice the rate: the stages' delays alone come to 93 samples there, half a sample short of a
		// whole one at the base rate, and a dry signal can only be delayed by whole samples to line up with this one
		var carry = _carry;
		_carry = middle[^1];
		for (var i = middle.Length - 1; i > 0; i--)
			middle[i] = middle[i - 1];
		middle[0] = carry;
		_down1.Down(middle, output);
	}

	private void Check(int baseFrames, int raisedFrames)
	{
		if (baseFrames > MaxBlockFrames)
			throw new ArgumentException($"A block of {baseFrames} frames is longer than the {MaxBlockFrames} this oversampler was made for.");
		if (raisedFrames != baseFrames * Factor)
			throw new ArgumentException($"At {Factor}× a block of {baseFrames} frames is {baseFrames * Factor} raised, not {raisedFrames}.");
	}

	private static float[] MakeHalfBand()
	{
		var h = new double[Taps];
		var sum = 0.0;
		for (var n = 0; n < Taps; n++)
		{
			var x = (n - Middle) / 2.0;
			// The sinc's zeros fall on the odd taps but the centre: exactly zero, as the steps assume, not sin(kπ)'s rounding
			var sinc = x == 0 ? 1.0 : (n - Middle) % 2 == 0 ? 0.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
			var w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * n / (Taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * n / (Taps - 1));
			h[n] = 0.5 * sinc * w;
			sum += h[n];
		}
		var taps = new float[Taps];
		for (var n = 0; n < Taps; n++)
			taps[n] = (float)(h[n] / sum);
		return taps;
	}

	private static float[] MakeReversed()
	{
		var reversed = new float[EvenTaps];
		for (var m = 0; m < EvenTaps; m++)
			reversed[m] = HalfBand[2 * (EvenTaps - 1 - m)];
		return reversed;
	}

	/// <summary>
	/// One 2× step. Up keeps the last 32 samples it was given; down keeps the last 32 even samples and 16 odd ones. Each
	/// history is kept twice over, so a window of it is always contiguous, oldest first.
	/// </summary>
	private sealed class Stage
	{
		private const int OddDepth = 16;
		private readonly float[] _even = new float[EvenTaps * 2];
		private readonly float[] _odd = new float[OddDepth * 2];
		private int _evenAt;
		private int _oddAt;

		private ReadOnlySpan<float> PushEven(float x)
		{
			_even[_evenAt] = x;
			_even[_evenAt + EvenTaps] = x;
			_evenAt = _evenAt + 1 == EvenTaps ? 0 : _evenAt + 1;
			return _even.AsSpan(_evenAt, EvenTaps);
		}

		private void PushOdd(float x)
		{
			_odd[_oddAt] = x;
			_odd[_oddAt + OddDepth] = x;
			_oddAt = _oddAt + 1 == OddDepth ? 0 : _oddAt + 1;
		}

		// A window oldest first through the even taps, from its first to just before its last
		private static float Sum(ReadOnlySpan<float> window, int from, int to, float sum)
		{
			var taps = Reversed.AsSpan(0, EvenTaps);
			for (var m = from; m < to; m++)
				sum += window[m] * taps[m];
			return sum;
		}

		// Between each pair of samples a zero: an even output meets the samples on the even taps, an odd one meets one
		// sample, on the centre tap
		public void Up(ReadOnlySpan<float> input, Span<float> output)
		{
			for (var i = 0; i < input.Length; i++)
			{
				var window = PushEven(input[i]);
				output[2 * i] = 2f * Sum(window, 0, EvenTaps, 0f);
				output[2 * i + 1] = 2f * (window[EvenTaps - 1 - Middle / 2] * Centre);
			}
		}

		// Keeps the even samples: each output is filtered right after the first of its pair arrives, so a pair of stages, up
		// and down, delays by a whole number of samples at the lower rate. It meets the even samples on the even taps and the
		// odd sample 31 back on the centre tap, which falls between the even taps 32 and 30
		public void Down(ReadOnlySpan<float> input, Span<float> output)
		{
			for (var i = 0; i < output.Length; i++)
			{
				var window = PushEven(input[2 * i]);
				var sum = Sum(window, 0, EvenTaps / 2, 0f);
				sum += _odd[_oddAt] * Centre;
				output[i] = Sum(window, EvenTaps / 2, EvenTaps, sum);
				PushOdd(input[2 * i + 1]);
			}
		}
	}
}
