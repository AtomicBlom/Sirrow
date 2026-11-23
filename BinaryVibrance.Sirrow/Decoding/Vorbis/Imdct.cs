namespace BinaryVibrance.Sirrow.Decoding.Vorbis;

/// <summary>
/// The inverse MDCT of one block size N: N/2 spectral lines in, N samples out,
/// <c>y[n] = sum over k of X[k] cos(2 pi / N (n + 1/2 + N/4)(k + 1/2))</c>, unscaled as libvorbis has it.
/// </summary>
/// <remarks>
/// Computed through a DCT-IV of size K = N/2, which a complex FFT of size N/4 gives. With c[p] = X[2p] and
/// d[p] = X[K-1-2p], z[p] = (c[p] - i d[p]) e^(i pi p / K); Z = the unscaled inverse DFT of z; S[q] = Z[q] e^(i pi (4q+1) / 4K).
/// Then the DCT-IV is u[2q] = Re S[q] and u[K-1-2q] = Im S[q], and the IMDCT unfolds it by the DCT-IV's symmetries:
/// y[n] = u[n + K/2] for n &lt; K/2, -u[3K/2 - 1 - n] up to 3K/2, and -u[n - 3K/2] to the end.
/// </remarks>
internal sealed class Imdct
{
	private readonly int _k;
	private readonly int _m;
	private readonly float[] _preCos;
	private readonly float[] _preSin;
	private readonly float[] _postCos;
	private readonly float[] _postSin;
	private readonly float[] _rootCos;
	private readonly float[] _rootSin;
	private readonly int[] _reversed;
	private readonly float[] _re;
	private readonly float[] _im;
	private readonly float[] _u;

	public Imdct(int n)
	{
		if (n < 16 || (n & (n - 1)) != 0)
			throw new ArgumentOutOfRangeException(nameof(n), n, "An IMDCT's size is a power of two, 16 or more.");
		Size = n;
		_k = n / 2;
		_m = n / 4;
		_preCos = new float[_m];
		_preSin = new float[_m];
		_postCos = new float[_m];
		_postSin = new float[_m];
		for (var p = 0; p < _m; p++)
		{
			var pre = Math.PI * p / _k;
			_preCos[p] = (float)Math.Cos(pre);
			_preSin[p] = (float)Math.Sin(pre);
			var post = Math.PI * (4 * p + 1) / (4.0 * _k);
			_postCos[p] = (float)Math.Cos(post);
			_postSin[p] = (float)Math.Sin(post);
		}
		_rootCos = new float[_m / 2];
		_rootSin = new float[_m / 2];
		for (var j = 0; j < _m / 2; j++)
		{
			_rootCos[j] = (float)Math.Cos(2 * Math.PI * j / _m);
			_rootSin[j] = (float)Math.Sin(2 * Math.PI * j / _m);
		}
		_reversed = new int[_m];
		var bits = VorbisBitReader.Ilog(_m) - 1;
		for (var i = 0; i < _m; i++)
		{
			var r = 0;
			for (var b = 0; b < bits; b++)
				r |= ((i >> b) & 1) << (bits - 1 - b);
			_reversed[i] = r;
		}
		_re = new float[_m];
		_im = new float[_m];
		_u = new float[_k];
	}

	/// <summary>N: the samples out.</summary>
	public int Size { get; }

	/// <summary>Transforms N/2 lines of <paramref name="spectrum"/> into N samples of <paramref name="output"/>.</summary>
	public void Inverse(ReadOnlySpan<float> spectrum, Span<float> output)
	{
		var k = _k;
		var m = _m;

		// Pre-twiddle, into bit-reversed order for the in-place FFT
		for (var p = 0; p < m; p++)
		{
			var c = spectrum[2 * p];
			var d = spectrum[k - 1 - 2 * p];
			var at = _reversed[p];
			_re[at] = c * _preCos[p] + d * _preSin[p];
			_im[at] = c * _preSin[p] - d * _preCos[p];
		}

		// Radix-2 FFT with the inverse DFT's sign, unscaled
		for (var size = 2; size <= m; size <<= 1)
		{
			var half = size >> 1;
			var stride = m / size;
			for (var start = 0; start < m; start += size)
			{
				for (var j = 0; j < half; j++)
				{
					var wr = _rootCos[j * stride];
					var wi = _rootSin[j * stride];
					var a = start + j;
					var b = a + half;
					var tr = _re[b] * wr - _im[b] * wi;
					var ti = _re[b] * wi + _im[b] * wr;
					_re[b] = _re[a] - tr;
					_im[b] = _im[a] - ti;
					_re[a] += tr;
					_im[a] += ti;
				}
			}
		}

		// Post-twiddle into the DCT-IV
		for (var q = 0; q < m; q++)
		{
			var re = _re[q] * _postCos[q] - _im[q] * _postSin[q];
			var im = _re[q] * _postSin[q] + _im[q] * _postCos[q];
			_u[2 * q] = re;
			_u[k - 1 - 2 * q] = im;
		}

		// Unfold to N samples
		var quarter = k / 2;
		for (var n = 0; n < quarter; n++)
			output[n] = _u[n + quarter];
		for (var n = quarter; n < 3 * quarter; n++)
			output[n] = -_u[3 * quarter - 1 - n];
		for (var n = 3 * quarter; n < 2 * k; n++)
			output[n] = -_u[n - 3 * quarter];
	}
}
