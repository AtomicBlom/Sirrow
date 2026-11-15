using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Effects;

/// <summary>
/// A room's sound: a feedback delay network of eight lines behind four all-passes that smear the input, each line
/// losing high frequencies as it goes round. Its defaults (all wet, no dry) suit a bus that only receives sends.
/// </summary>
public sealed class Reverb : AudioEffect
{
	private const int Lines = 8;
	private const float AllPassGain = 0.6f;
	private static readonly int[] LineBase = [1423, 1597, 1777, 1949, 2137, 2339, 2543, 2749];
	private static readonly int[] AllPassBase = [142, 107, 379, 277];

	private readonly float _size;
	private readonly DelayLine[] _lines = new DelayLine[Lines];
	private readonly int[] _lengths = new int[Lines];
	private readonly float[] _gains = new float[Lines];
	private readonly float[] _low = new float[Lines];
	private readonly float[] _out = new float[Lines];
	private readonly DelayLine[] _allPasses = new DelayLine[AllPassBase.Length];
	private readonly int[] _allPassLengths = new int[AllPassBase.Length];
	private DelayLine _pre = null!;

	/// <summary>A reverb whose room is <paramref name="size"/> times the default's, 0.5 to 2: its lines' lengths.</summary>
	public Reverb(float size = 1f) => _size = Math.Clamp(size, 0.5f, 2f);

	/// <summary>How long the tail takes to fall 60 dB, in seconds, 0.1 to 20. Default 1.8, eased over 0.1 s.</summary>
	public SmoothedParameter DecaySeconds { get; } = new(1.8f, 0.1f);

	/// <summary>Where the tail starts losing its highs, in hertz: lower is a softer, more furnished room. Default 6000.</summary>
	public AudioParameter DampingHz { get; } = new(6000f);

	/// <summary>The gap before the room answers, in seconds, 0 to 0.25. Default 0.015.</summary>
	public AudioParameter PreDelaySeconds { get; } = new(0.015f);

	/// <summary>The reverb's share of the output, a linear gain. Default 1.</summary>
	public SmoothedParameter Wet { get; } = new(1f);

	/// <summary>The input's share of the output, a linear gain. Default 0: a bus fed by sends has the dry sound elsewhere.</summary>
	public SmoothedParameter Dry { get; } = new(0f);

	/// <inheritdoc/>
	protected override void OnPrepare(int sampleRate)
	{
		var scale = sampleRate / 48000f;
		for (var i = 0; i < Lines; i++)
		{
			_lengths[i] = Math.Max(1, (int)MathF.Round(LineBase[i] * scale * _size));
			_lines[i] = new DelayLine(_lengths[i]);
		}
		for (var i = 0; i < AllPassBase.Length; i++)
		{
			_allPassLengths[i] = Math.Max(1, (int)MathF.Round(AllPassBase[i] * scale));
			_allPasses[i] = new DelayLine(_allPassLengths[i]);
		}
		_pre = new DelayLine((int)(0.25f * sampleRate) + 1);
		DecaySeconds.Prepare(sampleRate);
		Wet.Prepare(sampleRate);
		Dry.Prepare(sampleRate);
		DecaySeconds.Snap();
		Wet.Snap();
		Dry.Snap();
	}

	/// <inheritdoc/>
	public override void Process(Span<float> left, Span<float> right)
	{
		var rate = SampleRate;
		var n = left.Length;
		DecaySeconds.Begin();
		var t60 = Math.Clamp(DecaySeconds.Skip(n), 0.1f, 20f);
		for (var i = 0; i < Lines; i++)
			_gains[i] = MathF.Pow(10f, -3f * _lengths[i] / (t60 * rate));
		var damp = 1f - MathF.Exp(-2f * MathF.PI * DampingHz.Snap() / rate);
		var preFrames = Math.Clamp((int)MathF.Round(Math.Clamp(PreDelaySeconds.Snap(), 0f, 0.25f) * rate), 1, _pre.MaxFrames);
		Wet.Begin();
		Dry.Begin();

		var lines = _lines;
		var lengths = _lengths;
		var gains = _gains;
		var low = _low;
		var o = _out;
		for (var s = 0; s < n; s++)
		{
			var x = 0.5f * (left[s] + right[s]);
			var p = _pre.Read(preFrames);
			_pre.Write(x);
			x = p;
			for (var a = 0; a < _allPasses.Length; a++)
			{
				var line = _allPasses[a];
				var d = line.Read(_allPassLengths[a]);
				var v = x + AllPassGain * d;
				Denormal.Flush(ref v);
				line.Write(v);
				x = d - AllPassGain * v;
			}
			var sum = 0f;
			for (var i = 0; i < Lines; i++)
			{
				o[i] = lines[i].Read(lengths[i]);
				low[i] += damp * (o[i] - low[i]);
				sum += low[i];
			}
			var mean = 0.25f * sum;
			for (var i = 0; i < Lines; i++)
			{
				var v = x + (low[i] - mean) * gains[i];
				Denormal.Flush(ref v);
				lines[i].Write(v);
			}
			var wetL = 0.35f * (o[0] + o[2] - o[4] - o[6]);
			var wetR = 0.35f * (o[1] + o[3] - o[5] - o[7]);
			var wet = Wet.Next();
			var dry = Dry.Next();
			left[s] = dry * left[s] + wet * wetL;
			right[s] = dry * right[s] + wet * wetR;
		}
		for (var i = 0; i < Lines; i++)
			Denormal.Flush(ref low[i]);
	}
}
