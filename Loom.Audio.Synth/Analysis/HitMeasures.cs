using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Analysis;

/// <summary>
/// A struck sound taken apart, to design a patch like it: where each hit starts, how its pitch falls over its first
/// moments, the tones that ring in it, how bright it is as it fades, each band's level over time and how noisy it is, and
/// how long it takes to die. Measures of one channel of samples at a rate. Like <see cref="AudioAnalysis"/>, they allocate
/// and are for tools and tests, never the audio thread.
/// </summary>
public static class HitMeasures
{
	/// <summary>
	/// Where each hit starts, by sample: the loudest sample of the last 20 ms (which a low drum's own cycles can't fool)
	/// jumping 9 dB over where it stood 20 ms before, within 35 dB of the loudest of all, at least 50 ms after the last hit;
	/// the hit then starts at the first sample of the jump a third of the way to its peak. Sound from the first sample on
	/// starts with a hit.
	/// </summary>
	public static List<int> Onsets(ReadOnlySpan<float> mono, int rate)
	{
		var hop = rate / 400;   // 2.5 ms
		var span = rate / 50;   // 20 ms
		var frames = mono.Length / hop;
		var level = new float[frames];
		for (var f = 0; f < frames; f++)
		{
			var from = Math.Max(0, (f + 1) * hop - span);
			level[f] = AudioAnalysis.ToDb(AudioAnalysis.Peak(mono.Slice(from, (f + 1) * hop - from)));
		}
		var floor = level.Length == 0 ? 0f : level.Max() - 35f;
		var onsets = new List<int>();
		var last = -1000;
		for (var f = 0; f < frames; f++)
		{
			if (level[f] < floor || f - last < 20)
				continue;
			var before = f >= 8 ? level[f - 8] : -200f;
			if (level[f] - before < 9f)
				continue;
			var start = Math.Max(0, f * hop - span);
			var end = (f + 1) * hop;
			var peak = AudioAnalysis.Peak(mono.Slice(start, end - start));
			var at = start;
			while (at < end && MathF.Abs(mono[at]) < peak / 3f)
				at++;
			onsets.Add(at);
			last = f;
		}
		if (onsets.Count == 0 && mono.Length > 0)
			onsets.Add(0);
		return onsets;
	}

	/// <summary>
	/// The pitch of what is under <paramref name="belowHz"/> at each of <paramref name="atMs"/>: the median of the
	/// half-cycles (zero crossings either way) within 3 ms of it, or the nearest one where none is. NaN where it has fallen
	/// 30 dB under its loudest or not yet started; below 0 where the half-cycles disagree too much to be a pitch (noise).
	/// </summary>
	public static float[] Pitch(ReadOnlySpan<float> hit, int rate, ReadOnlySpan<float> atMs, float belowHz = 600f)
	{
		var low = new float[hit.Length];
		Biquad a = default, b = default;
		a.SetLowPass(belowHz, 0.707f, rate);
		b.SetLowPass(belowHz, 0.707f, rate);
		for (var i = 0; i < hit.Length; i++)
			low[i] = b.Tick(a.Tick(hit[i]));
		var top = AudioAnalysis.Peak(low);
		var crossings = new List<float>();
		for (var i = 1; i < low.Length; i++)
			if ((low[i - 1] <= 0f) != (low[i] <= 0f))
				crossings.Add(i - 1 + low[i - 1] / (low[i - 1] - low[i]));
		var result = new float[atMs.Length];
		for (var t = 0; t < atMs.Length; t++)
		{
			var at = atMs[t] * rate / 1000f;
			var near = rate * 0.003f;
			var halves = new List<float>();
			var nearest = float.NaN;
			var nearestBy = float.MaxValue;
			for (var c = 1; c < crossings.Count; c++)
			{
				var from = crossings[c - 1];
				var to = crossings[c];
				var middle = (from + to) / 2f;
				if (AudioAnalysis.Peak(low.AsSpan((int)from, Math.Max(1, (int)to - (int)from))) < top * 0.03f)
					continue;
				var half = rate / (2f * (to - from));
				if (MathF.Abs(middle - at) <= near)
					halves.Add(half);
				if (MathF.Abs(middle - at) < nearestBy && from <= at + rate * 0.02f && to >= at - rate * 0.02f)
				{
					nearestBy = MathF.Abs(middle - at);
					nearest = half;
				}
			}
			var sorted = halves.Order().ToArray();
			// Half-cycles that disagree by half again between their quartiles are noise, not a pitch: marked below 0
			result[t] = sorted.Length >= 4 && sorted[sorted.Length * 3 / 4] > 1.5f * sorted[sorted.Length / 4]
				? -1f
				: sorted.Length > 0 ? sorted[sorted.Length / 2] : nearest;
		}
		return result;
	}

	/// <summary>
	/// The tones that stand out in the body (from 5 ms, 4096 samples of it): peaks of the spectrum 10 dB over the median
	/// round them, within 30 dB of the strongest, strongest first, at most <paramref name="most"/>. None for a body too short.
	/// </summary>
	public static List<Tone> Tones(ReadOnlySpan<float> hit, int rate, int most)
	{
		var start = Math.Min(hit.Length, rate / 200);
		var length = Math.Min(4096, hit.Length - start);
		if (length < 512)
			return [];
		var body = new float[4096];
		hit.Slice(start, length).CopyTo(body);
		var spectrum = AudioAnalysis.Spectrum(body);
		var top = spectrum.Max();
		var peaks = new List<Tone>();
		for (var k = 2; k < spectrum.Length - 2; k++)
		{
			var v = spectrum[k];
			if (v < top * 0.03f || v < spectrum[k - 1] || v < spectrum[k + 1] || v < spectrum[k - 2] || v < spectrum[k + 2])
				continue;
			var round = spectrum[Math.Max(1, k - 20)..Math.Min(spectrum.Length, k + 21)].Order().ToArray();
			if (v < round[round.Length / 2] * 3.16f)
				continue;
			// Between bins, by a parabola through the logs of it and its neighbours
			var l = MathF.Log(MathF.Max(spectrum[k - 1], 1e-12f));
			var m = MathF.Log(v);
			var r = MathF.Log(MathF.Max(spectrum[k + 1], 1e-12f));
			var shift = l - 2f * m + r == 0f ? 0f : 0.5f * (l - r) / (l - 2f * m + r);
			peaks.Add(new Tone((k + shift) * rate / 4096, AudioAnalysis.ToDb(v / top)));
		}
		return [.. peaks.OrderByDescending(p => p.Db).Take(most)];
	}

	/// <summary>
	/// How bright it is from <paramref name="fromMs"/> on: the centre of the spectrum of <paramref name="window"/> samples,
	/// weighted by power, in hertz. NaN where the window runs past the end.
	/// </summary>
	public static float Brightness(ReadOnlySpan<float> samples, int rate, float fromMs, int window)
	{
		var start = (int)(fromMs * rate / 1000f);
		if (start + window > samples.Length)
			return float.NaN;
		var spectrum = AudioAnalysis.Spectrum(samples.Slice(start, window));
		double sum = 0, weighted = 0;
		for (var k = 1; k < spectrum.Length; k++)
		{
			var power = (double)spectrum[k] * spectrum[k];
			sum += power;
			weighted += power * k * rate / (double)window;
		}
		return sum > 0 ? (float)(weighted / sum) : float.NaN;
	}

	/// <summary>
	/// How noisy each band is in the body (from 5 ms, 4096 samples of it): its spectrum's flatness, the power's geometric
	/// mean over its mean, 1 for noise and near 0 for a tone or a few.
	/// </summary>
	public static float[] Flatness(ReadOnlySpan<float> hit, int rate, ReadOnlySpan<(float Low, float High)> bands)
	{
		var start = Math.Min(hit.Length, rate / 200);
		var body = new float[4096];
		hit.Slice(start, Math.Min(4096, hit.Length - start)).CopyTo(body);
		var spectrum = AudioAnalysis.Spectrum(body);
		var result = new float[bands.Length];
		for (var b = 0; b < bands.Length; b++)
		{
			var from = Math.Max(1, (int)(bands[b].Low * 4096 / rate));
			var to = Math.Min(spectrum.Length - 1, (int)(bands[b].High * 4096 / rate));
			double logSum = 0, sum = 0;
			var n = 0;
			for (var k = from; k <= to; k++)
			{
				var power = Math.Max(1e-20, (double)spectrum[k] * spectrum[k]);
				logSum += Math.Log(power);
				sum += power;
				n++;
			}
			result[b] = n > 0 && sum > 0 ? (float)(Math.Exp(logSum / n) / (sum / n)) : float.NaN;
		}
		return result;
	}

	/// <summary>
	/// A band's level over the hit, its RMS every <paramref name="ms"/> milliseconds, after high- and low-passing twice each
	/// at its edges.
	/// </summary>
	public static float[] BandEnvelope(ReadOnlySpan<float> hit, int rate, float low, float high, float ms = 2f)
	{
		Biquad h1 = default, h2 = default, l1 = default, l2 = default;
		h1.SetHighPass(low, 0.707f, rate);
		h2.SetHighPass(low, 0.707f, rate);
		l1.SetLowPass(Math.Min(high, rate * 0.45f), 0.707f, rate);
		l2.SetLowPass(Math.Min(high, rate * 0.45f), 0.707f, rate);
		var frame = (int)(rate * ms / 1000f);
		var result = new float[hit.Length / frame];
		for (var f = 0; f < result.Length; f++)
		{
			double sum = 0;
			for (var i = f * frame; i < (f + 1) * frame; i++)
			{
				var y = l2.Tick(l1.Tick(h2.Tick(h1.Tick(hit[i]))));
				sum += y * y;
			}
			result[f] = (float)Math.Sqrt(sum / frame);
		}
		return result;
	}

	/// <summary>How long until it has fallen for good under <paramref name="share"/> of its peak, in seconds: 0.01 is 40 dB down.</summary>
	public static double Tail(ReadOnlySpan<float> samples, int rate, float share = 0.01f)
	{
		var peak = AudioAnalysis.Peak(samples);
		var last = 0;
		for (var i = 0; i < samples.Length; i++)
			if (MathF.Abs(samples[i]) > peak * share)
				last = i;
		return (double)last / rate;
	}
}
