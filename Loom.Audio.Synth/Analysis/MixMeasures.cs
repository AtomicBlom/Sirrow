namespace Loom.Audio.Synth.Analysis;

/// <summary>
/// A rendered mix measured, so a change to it can be judged without ears: where its energy lies, how bright, how squashed
/// and how wide it is. Measures of interleaved stereo samples at a rate. Like <see cref="AudioAnalysis"/>, they allocate and
/// are for tools and tests, never the audio thread.
/// </summary>
public static class MixMeasures
{
	private const int Size = 8192;

	/// <summary>
	/// Its balance, from the left channel in blocks of 8192: each band's share of the energy between
	/// <paramref name="bandEdges"/> (rising, in hertz; one more band than edges), the spectrum's centre over the blocks that
	/// sound (quartiles and median), the median of each second's peak over its RMS, and how alike the two sides are. Null
	/// when it is silent.
	/// </summary>
	public static Balance? Balance(ReadOnlySpan<float> stereo, int rate, ReadOnlySpan<float> bandEdges)
	{
		var frames = stereo.Length / 2;
		var left = new float[frames];
		double ll = 0, rr = 0, lr = 0;
		for (var i = 0; i < frames; i++)
		{
			float l = stereo[2 * i], r = stereo[2 * i + 1];
			left[i] = l;
			ll += l * l;
			rr += r * r;
			lr += l * r;
		}

		var bands = new double[bandEdges.Length + 1];
		var centres = new List<double>();
		for (var start = 0; start + Size <= frames; start += Size)
		{
			var spectrum = AudioAnalysis.Spectrum(left.AsSpan(start, Size));
			double total = 0, moment = 0;
			for (var k = 1; k < spectrum.Length; k++)
			{
				var hz = (double)k * rate / Size;
				var power = (double)spectrum[k] * spectrum[k];
				var band = 0;
				while (band < bandEdges.Length && hz >= bandEdges[band])
					band++;
				bands[band] += power;
				total += power;
				moment += power * hz;
			}
			if (total > 1e-9)
				centres.Add(moment / total);
		}

		var crests = new List<double>();
		for (var start = 0; start + rate <= frames; start += rate)
		{
			double peak = 0, squares = 0;
			for (var i = start; i < start + rate; i++)
			{
				peak = Math.Max(peak, Math.Abs(left[i]));
				squares += left[i] * left[i];
			}
			if (squares / rate > 1e-7)
				crests.Add(20 * Math.Log10(peak / Math.Sqrt(squares / rate)));
		}

		var all = bands.Sum();
		if (all <= 0 || centres.Count == 0)
			return null;
		var shares = new double[bands.Length];
		for (var b = 0; b < bands.Length; b++)
			shares[b] = bands[b] / all;
		centres.Sort();
		crests.Sort();
		return new Balance(shares, centres[centres.Count / 4], centres[centres.Count / 2], centres[centres.Count * 3 / 4],
			crests.Count > 0 ? crests[crests.Count / 2] : double.NaN, lr / Math.Sqrt(Math.Max(ll * rr, 1e-30)));
	}

	/// <summary>
	/// A stretch of it in brief, from the left channel: its RMS and peak, the spectrum's centre, and its energy's shares
	/// under 250 Hz, from there to 1 kHz, and above. Null when it is silent.
	/// </summary>
	public static Spread? Spread(ReadOnlySpan<float> stereo, int rate)
	{
		var frames = stereo.Length / 2;
		var left = new float[frames];
		double squares = 0, peak = 0;
		for (var i = 0; i < frames; i++)
		{
			left[i] = stereo[2 * i];
			squares += left[i] * left[i];
			peak = Math.Max(peak, Math.Abs(left[i]));
		}
		double low = 0, middle = 0, high = 0, moment = 0;
		for (var start = 0; start + Size <= frames; start += Size)
		{
			var spectrum = AudioAnalysis.Spectrum(left.AsSpan(start, Size));
			for (var k = 1; k < spectrum.Length; k++)
			{
				var hz = (double)k * rate / Size;
				var power = (double)spectrum[k] * spectrum[k];
				if (hz < 250)
					low += power;
				else if (hz < 1000)
					middle += power;
				else
					high += power;
				moment += power * hz;
			}
		}
		var all = low + middle + high;
		if (all <= 0 || frames == 0)
			return null;
		return new Spread(Math.Sqrt(squares / frames), peak, moment / all, low / all, middle / all, high / all);
	}
}
