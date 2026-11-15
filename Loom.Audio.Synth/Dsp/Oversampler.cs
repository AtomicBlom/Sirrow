namespace Loom.Audio.Synth.Dsp;

/// <summary>
/// Raises a block to 2 or 4 times the rate and brings it back down, so a waveshaper run between the two (distortion,
/// saturation) doesn't fold its new harmonics back below the rate as aliasing. Each 2× step is a 63-tap half-band
/// filter (a Blackman-windowed sinc); a factor of 4 is two steps each way. One per channel; it allocates only here.
/// </summary>
public sealed class Oversampler
{
	private const int Taps = 63;
	private static readonly float[] HalfBand = MakeHalfBand();

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
			var x = (n - (Taps - 1) / 2) / 2.0;
			var sinc = x == 0 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
			var w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * n / (Taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * n / (Taps - 1));
			h[n] = 0.5 * sinc * w;
			sum += h[n];
		}
		var taps = new float[Taps];
		for (var n = 0; n < Taps; n++)
			taps[n] = (float)(h[n] / sum);
		return taps;
	}

	/// <summary>One 2× step: its own history of the last 63 samples at the raised rate, kept twice over so a window of them is always contiguous.</summary>
	private sealed class Stage
	{
		private readonly float[] _history = new float[Taps * 2];
		private int _position;

		private void Push(float x)
		{
			_history[_position] = x;
			_history[_position + Taps] = x;
			_position = _position + 1 == Taps ? 0 : _position + 1;
		}

		private float Filter()
		{
			// The newest sample sits just before _position; the window runs oldest first from it.
			var window = _history.AsSpan(_position, Taps);
			var sum = 0f;
			for (var n = 0; n < Taps; n++)
				sum += window[n] * HalfBand[Taps - 1 - n];
			return sum;
		}

		public void Up(ReadOnlySpan<float> input, Span<float> output)
		{
			for (var i = 0; i < input.Length; i++)
			{
				Push(input[i]);
				output[2 * i] = 2f * Filter();
				Push(0f);
				output[2 * i + 1] = 2f * Filter();
			}
		}

		public void Down(ReadOnlySpan<float> input, Span<float> output)
		{
			for (var i = 0; i < output.Length; i++)
			{
				Push(input[2 * i]);
				Push(input[2 * i + 1]);
				output[i] = Filter();
			}
		}
	}
}
