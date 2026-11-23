namespace BinaryVibrance.Sirrow;

/// <summary>Measures of a rendered signal, for tests and reports: level, peak, the level at a frequency, the strongest frequency.</summary>
public static class AudioAnalysis
{
	/// <summary>The largest magnitude among <paramref name="samples"/>: 0 for none.</summary>
	public static float Peak(ReadOnlySpan<float> samples)
	{
		var peak = 0f;
		foreach (var x in samples)
			peak = MathF.Max(peak, MathF.Abs(x));
		return peak;
	}

	/// <summary>The root of the mean of the squares of <paramref name="samples"/>: 0 for none.</summary>
	public static float Rms(ReadOnlySpan<float> samples)
	{
		if (samples.IsEmpty)
			return 0f;
		var sum = 0.0;
		foreach (var x in samples)
			sum += (double)x * x;
		return (float)Math.Sqrt(sum / samples.Length);
	}

	/// <summary>An amplitude in decibels, 20·log10: an amplitude under 1e-10 reads as 1e-10, -200 dB.</summary>
	public static float ToDb(float amplitude) => 20f * MathF.Log10(MathF.Max(amplitude, 1e-10f));

	/// <summary>The amplitude of a level in decibels, 10^(db/20).</summary>
	public static float FromDb(float db) => MathF.Pow(10f, db / 20f);

	/// <summary>
	/// The amplitude of the sinusoid at <paramref name="frequency"/> in <paramref name="mono"/>, through a Hann window over
	/// the whole span: a sine of amplitude A at exactly that frequency reads A.
	/// </summary>
	/// <param name="mono">One channel's samples.</param>
	/// <param name="frequency">The frequency to measure, in hertz.</param>
	/// <param name="sampleRate">The samples' rate, in frames per second.</param>
	public static float LevelAt(ReadOnlySpan<float> mono, double frequency, int sampleRate)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		if (mono.Length < 2)
			throw new ArgumentException("A level needs at least two samples.", nameof(mono));
		double re = 0, im = 0, windowSum = 0;
		var step = 2 * Math.PI * frequency / sampleRate;
		for (var n = 0; n < mono.Length; n++)
		{
			var w = Hann(n, mono.Length);
			windowSum += w;
			re += mono[n] * w * Math.Cos(step * n);
			im += mono[n] * w * Math.Sin(step * n);
		}
		return (float)(2 * Math.Sqrt(re * re + im * im) / windowSum);
	}

	/// <summary>
	/// The amplitudes of the spectrum's bins 0 to N/2, over the first N samples of <paramref name="mono"/>, N the largest
	/// power of two that fits, through a Hann window: a sine of amplitude A on a bin's centre reads A at that bin. Bin k is
	/// at k·rate/N hertz.
	/// </summary>
	/// <exception cref="ArgumentException">Fewer than two samples.</exception>
	public static float[] Spectrum(ReadOnlySpan<float> mono)
	{
		if (mono.Length < 2)
			throw new ArgumentException("A spectrum needs at least two samples.", nameof(mono));
		var n = 1 << (31 - int.LeadingZeroCount(mono.Length));
		var bins = new float[n / 2 + 1];
		Spectrum(mono, new double[n], new double[n], bins);
		return bins;
	}

	/// <summary>
	/// The same spectrum, over the first N samples of <paramref name="mono"/>, N the length of <paramref name="real"/>, into
	/// <paramref name="bins"/>, with <paramref name="real"/> and <paramref name="imaginary"/> as its working space: so a
	/// view drawing it every frame keeps its buffers and allocates nothing.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="real"/> isn't a power of two of at least two, or
	/// <paramref name="imaginary"/> isn't as long, <paramref name="mono"/> is shorter, or <paramref name="bins"/> isn't
	/// N/2 + 1 long.</exception>
	public static void Spectrum(ReadOnlySpan<float> mono, Span<double> real, Span<double> imaginary, Span<float> bins)
	{
		var n = real.Length;
		if (n < 2 || (n & (n - 1)) != 0)
			throw new ArgumentException("A spectrum's working space is a power of two long, at least two.", nameof(real));
		if (imaginary.Length != n)
			throw new ArgumentException("The imaginary part is as long as the real part.", nameof(imaginary));
		if (mono.Length < n)
			throw new ArgumentException($"A spectrum over {n} samples needs at least that many; there are {mono.Length}.", nameof(mono));
		if (bins.Length != n / 2 + 1)
			throw new ArgumentException($"A spectrum over {n} samples has {n / 2 + 1} bins.", nameof(bins));
		var windowSum = 0.0;
		for (var i = 0; i < n; i++)
		{
			var w = Hann(i, n);
			windowSum += w;
			real[i] = mono[i] * w;
		}
		imaginary.Clear();
		Fft(real, imaginary);
		for (var k = 0; k < bins.Length; k++)
			bins[k] = (float)(2 * Math.Sqrt(real[k] * real[k] + imaginary[k] * imaginary[k]) / windowSum);
	}

	/// <summary>
	/// The frequency of the strongest bin of <see cref="Spectrum(ReadOnlySpan{float})"/> between <paramref name="minHz"/> and
	/// <paramref name="maxHz"/>, refined between bins by a parabola through the logs of it and its neighbours. A strongest
	/// bin at either end of the range reads as its centre.
	/// </summary>
	/// <param name="mono">One channel's samples.</param>
	/// <param name="sampleRate">The samples' rate, in frames per second.</param>
	/// <param name="minHz">The lowest frequency to look at.</param>
	/// <param name="maxHz">The highest frequency to look at.</param>
	/// <exception cref="ArgumentException">No bin lies in the range.</exception>
	public static double PeakFrequency(ReadOnlySpan<float> mono, int sampleRate, double minHz, double maxHz)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		var spectrum = Spectrum(mono);
		var n = (spectrum.Length - 1) * 2;
		var binHz = (double)sampleRate / n;
		var first = Math.Max(0, (int)Math.Ceiling(minHz / binHz));
		var last = Math.Min(spectrum.Length - 1, (int)Math.Floor(maxHz / binHz));
		if (first > last)
			throw new ArgumentException($"No bin of a {n}-point spectrum at {sampleRate} Hz lies between {minHz} and {maxHz} Hz.");

		var best = first;
		for (var k = first + 1; k <= last; k++)
			if (spectrum[k] > spectrum[best])
				best = k;
		if (best == first || best == last)
			return best * binHz;

		var a = Math.Log(Math.Max(spectrum[best - 1], 1e-30f));
		var b = Math.Log(Math.Max(spectrum[best], 1e-30f));
		var c = Math.Log(Math.Max(spectrum[best + 1], 1e-30f));
		var denominator = a - 2 * b + c;
		var d = denominator == 0 ? 0 : 0.5 * (a - c) / denominator;
		return (best + d) * binHz;
	}

	private static double Hann(int n, int length) => 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (length - 1));

	// An iterative radix-2 FFT in place; re and im are a power of two long
	private static void Fft(Span<double> re, Span<double> im)
	{
		var n = re.Length;
		if (im.Length != n)
			throw new ArgumentException("The real and imaginary parts are as long as each other.", nameof(im));
		for (int i = 1, j = 0; i < n; i++)
		{
			var bit = n >> 1;
			for (; (j & bit) != 0; bit >>= 1)
				j ^= bit;
			j ^= bit;
			if (i < j)
			{
				(re[i], re[j]) = (re[j], re[i]);
				(im[i], im[j]) = (im[j], im[i]);
			}
		}
		for (var size = 2; size <= n; size <<= 1)
		{
			var angle = -2 * Math.PI / size;
			var half = size / 2;
			for (var start = 0; start < n; start += size)
			{
				for (var k = 0; k < half; k++)
				{
					var wr = Math.Cos(angle * k);
					var wi = Math.Sin(angle * k);
					var i = start + k;
					var j = i + half;
					var tr = re[j] * wr - im[j] * wi;
					var ti = re[j] * wi + im[j] * wr;
					re[j] = re[i] - tr;
					im[j] = im[i] - ti;
					re[i] += tr;
					im[i] += ti;
				}
			}
		}
	}
}
