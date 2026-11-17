namespace Loom.Audio.Synth.Analysis;

/// <summary>
/// A pitched note taken apart, struck or plucked, to design a patch like it: its partials, each one's pitch and level,
/// how each rings early and later and how much it beats, and how bright its strike is. Measures of one channel of samples
/// at a rate. Like <see cref="AudioAnalysis"/>, they allocate and are for tools and tests, never the audio thread.
/// </summary>
public static class NoteMeasures
{
	/// <summary>
	/// The partials that stand out in the note's body (from 20 ms, up to 16384 samples of it): peaks of the spectrum 8 dB
	/// over the median round them and within 50 dB of the strongest, the sixteen strongest in order of pitch.
	/// <paramref name="f0"/> is the note: the lowest within 20 dB of the strongest. If most of them are its harmonics, the
	/// note's first twenty harmonics instead, each the strongest peak within 2.5% of where it should be (NaN where none
	/// is), and the note from the first. None for a body too short or with nothing standing out.
	/// </summary>
	public static List<Partial> Partials(ReadOnlySpan<float> note, int rate, out float f0)
	{
		var spectrum = BodySpectrum(note, rate, out var size);
		f0 = float.NaN;
		if (spectrum.Length == 0)
			return [];
		var top = spectrum.Max();
		var peaks = new List<Partial>();
		for (var k = 4; k < spectrum.Length - 4; k++)
		{
			var v = spectrum[k];
			if (k * (float)rate / size < 40f || v < top * 0.0032f)
				continue;
			var highest = true;
			for (var d = -3; d <= 3 && highest; d++)
				highest = d == 0 || spectrum[k + d] <= v;
			if (!highest)
				continue;
			var round = spectrum[Math.Max(1, k - 60)..Math.Min(spectrum.Length, k + 61)].Order().ToArray();
			if (v < round[round.Length / 2] * 2.5f)
				continue;
			peaks.Add(new(Interpolate(spectrum, k) * rate / size, AudioAnalysis.ToDb(v / top), 0));
		}
		if (peaks.Count == 0)
			return [];
		var strongest = peaks.OrderByDescending(p => p.Db).Take(16).OrderBy(p => p.Hz).ToList();
		var note0 = strongest.First(p => p.Db > -20f).Hz;
		f0 = note0;
		var harmonics = strongest.Count(p =>
		{
			var n = MathF.Round(p.Hz / note0);
			return n >= 1f && MathF.Abs(p.Hz / (n * note0) - 1f) < 0.02f;
		});
		if (harmonics < 0.7f * strongest.Count)
			return strongest;

		var result = new List<Partial>();
		for (var n = 1; n <= 20; n++)
		{
			var from = (int)(n * note0 * 0.975f * size / rate);
			var to = (int)Math.Ceiling(n * note0 * 1.025f * size / rate);
			if (to >= spectrum.Length - 1)
				break;
			var best = from;
			for (var k = from; k <= to; k++)
				if (spectrum[k] > spectrum[best])
					best = k;
			var standsOut = spectrum[best] > top * 0.001f && spectrum[best] >= spectrum[best - 1] && spectrum[best] >= spectrum[best + 1];
			result.Add(standsOut ? new(Interpolate(spectrum, best) * rate / size, AudioAnalysis.ToDb(spectrum[best] / top), n) : new(float.NaN, float.NaN, n));
		}
		// The note from its harmonics' own spacing, which a stretched string's upper harmonics would pull sharp
		if (!float.IsNaN(result[0].Hz))
			f0 = result[0].Hz;
		return result;
	}

	/// <summary>
	/// How a partial at <paramref name="hz"/> rings: its level frame by frame (a 125 ms window every 10 ms, timed by the
	/// window's middle), its peak and when, how long it would take to fall 60 dB at the rate it falls over its first 300 ms
	/// from the peak and at the rate after, until it reaches the floor (50 dB down, or 25 dB down and falling under 1 dB in
	/// 300 ms); and, after the first 300 ms, how far its level strays from that fall and how often it crosses it, as beats a
	/// second. NaNs where the note is too short.
	/// </summary>
	public static PartialRing Ring(ReadOnlySpan<float> note, int rate, float hz)
	{
		var frame = rate / 8;
		var hop = rate / 100;
		var hopMs = 1000f * hop / rate;
		var centreMs = 1000f * frame / 2 / rate;
		var count = Math.Max(0, (note.Length - frame) / hop + 1);
		if (count < 8 || float.IsNaN(hz))
			return new(float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
		var track = new float[count];
		for (var f = 0; f < count; f++)
			track[f] = AudioAnalysis.ToDb(AudioAnalysis.LevelAt(note.Slice(f * hop, frame), hz, rate));
		var peakF = 0;
		for (var f = 1; f < Math.Min(track.Length, 150); f++)
			if (track[f] > track[peakF])
				peakF = f;
		var peakDb = track[peakF];
		var stop = track.Length;
		for (var f = peakF + 1; f < track.Length; f++)
		{
			if (track[f] < peakDb - 50f)
			{
				stop = f;
				break;
			}
			if (f >= peakF + 30 && track[f] < peakDb - 25f && Smooth(track, f - 30) - Smooth(track, f) < 1f)
			{
				stop = f - 30;
				break;
			}
		}
		var early = Slope(track, peakF, Math.Min(stop, peakF + 30), hopMs, out _, out _);
		if (stop - (peakF + 30) < 20)
			return new(peakDb, centreMs + peakF * hopMs, Sixty(early), float.NaN, float.NaN, float.NaN);
		var late = Slope(track, peakF + 30, stop, hopMs, out var wobble, out var crossings);
		var beat = crossings / 2f / ((stop - peakF - 30) * hopMs / 1000f);
		return new(peakDb, centreMs + peakF * hopMs, Sixty(early), Sixty(late), wobble, beat);
	}

	/// <summary>
	/// How much more is above 3 kHz in the first 15 ms than 60 to 75 ms in, in decibels: the strike's own top. NaN for a
	/// note under 75 ms.
	/// </summary>
	public static float Strike(ReadOnlySpan<float> note, int rate)
	{
		float High(ReadOnlySpan<float> part)
		{
			var spectrum = AudioAnalysis.Spectrum(part);
			double sum = 0;
			for (var k = (int)(3000f * part.Length / rate); k < spectrum.Length; k++)
				sum += (double)spectrum[k] * spectrum[k];
			return (float)sum;
		}
		var length = rate * 15 / 1000;
		if (note.Length < rate * 75 / 1000)
			return float.NaN;
		return 10f * MathF.Log10(MathF.Max(High(note[..length]), 1e-20f) / MathF.Max(High(note.Slice(rate * 60 / 1000, length)), 1e-20f));
	}

	// The spectrum of the note's body: from 20 ms, the longest power of two up to 16384 samples that fits, at least 4096
	private static float[] BodySpectrum(ReadOnlySpan<float> note, int rate, out int size)
	{
		var start = rate / 50;
		size = 16384;
		while (size > 4096 && start + size > note.Length)
			size /= 2;
		var body = new float[size];
		note.Slice(start, Math.Min(size, note.Length - start)).CopyTo(body);
		return AudioAnalysis.Spectrum(body);
	}

	// A bin's peak between its neighbours, by a parabola through their logs: in bins
	private static float Interpolate(float[] spectrum, int k)
	{
		var l = MathF.Log(MathF.Max(spectrum[k - 1], 1e-12f));
		var m = MathF.Log(MathF.Max(spectrum[k], 1e-12f));
		var r = MathF.Log(MathF.Max(spectrum[k + 1], 1e-12f));
		var denominator = l - 2f * m + r;
		return k + (denominator == 0f ? 0f : 0.5f * (l - r) / denominator);
	}

	// The level averaged over five frames from f
	private static float Smooth(float[] track, int f)
	{
		var sum = 0f;
		var n = 0;
		for (var i = f; i < Math.Min(track.Length, f + 5); i++, n++)
			sum += track[i];
		return n > 0 ? sum / n : float.NaN;
	}

	// The least-squares slope of a track between two frames, in dB a second; with the residuals' standard deviation and
	// how often they change sign
	private static float Slope(float[] track, int from, int to, float hopMs, out float deviation, out int crossings)
	{
		deviation = float.NaN;
		crossings = 0;
		var n = to - from;
		if (n < 5)
			return float.NaN;
		double sx = 0, sy = 0, sxx = 0, sxy = 0;
		for (var f = from; f < to; f++)
		{
			double x = (f - from) * hopMs / 1000.0, y = track[f];
			sx += x;
			sy += y;
			sxx += x * x;
			sxy += x * y;
		}
		var slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
		var intercept = (sy - slope * sx) / n;
		double squares = 0;
		var lastSign = 0;
		for (var f = from; f < to; f++)
		{
			var residual = track[f] - (intercept + slope * (f - from) * hopMs / 1000.0);
			squares += residual * residual;
			var sign = Math.Sign(residual);
			if (sign != 0 && lastSign != 0 && sign != lastSign)
				crossings++;
			if (sign != 0)
				lastSign = sign;
		}
		deviation = (float)Math.Sqrt(squares / n);
		return (float)slope;
	}

	// The seconds to fall 60 dB at a slope in dB a second; infinite for one that doesn't fall
	private static float Sixty(float slope) => float.IsNaN(slope) ? float.NaN : slope >= -0.5f ? float.PositiveInfinity : -60f / slope;
}
