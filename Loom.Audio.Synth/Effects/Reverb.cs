using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Effects;

/// <summary>
/// A room's sound: a feedback delay network of eight lines behind four all-passes that smear the input, each line
/// losing high frequencies as it goes round. Four of the lines slowly change length by a few samples, so a long tail
/// doesn't ring on at a few frequencies of the network's own. Its defaults (all wet, no dry) suit a bus that only
/// receives sends.
/// </summary>
/// <remarks>
/// Each time round, every line is fed from all eight in equal measure (a Hadamard mix). That is what lets four swaying
/// lines move the whole tail: fed mostly from itself (a Householder mix), a still line keeps its own frequencies
/// whatever the others do, and the sway measured as doing little. The mix alone changes nothing that was measured:
/// how dense a click's tail is from the start and how much it repeats itself are the same either way, since the
/// all-passes on the way in have already spread a click out before the lines come round.
/// </remarks>
public sealed class Reverb : AudioEffect
{
	private const int Lines = 8;
	private const float AllPassGain = 0.6f;
	// The most a swaying line's length moves either way, in frames at 48 kHz, and how often its sway is worked out
	private const float SwayFrames = 10f;
	private const int SwayEvery = 16;
	// 1 / √8: what keeps the mix from adding or losing energy
	private const float MixScale = 0.35355339f;
	private static readonly int[] LineBase = [1423, 1597, 1777, 1949, 2137, 2339, 2543, 2749];
	private static readonly int[] AllPassBase = [142, 107, 379, 277];
	// The swaying lines' rates, in cycles a second: none a multiple of another, so they never sway together
	private static readonly float[] SwayHz = [0.37f, 0.53f, 0.71f, 0.89f];

	private readonly float _size;
	private readonly DelayLine[] _lines = new DelayLine[Lines];
	private readonly int[] _lengths = new int[Lines];
	private readonly float[] _gains = new float[Lines];
	private readonly float[] _low = new float[Lines];
	private readonly float[] _out = new float[Lines];
	private readonly float[] _mixed = new float[Lines];
	private readonly float[] _sway = new float[SwayHz.Length];
	private readonly Lfo[] _swayLfo = new Lfo[SwayHz.Length];
	private readonly DelayLine[] _allPasses = new DelayLine[AllPassBase.Length];
	private readonly int[] _allPassLengths = new int[AllPassBase.Length];
	private DelayLine _pre = null!;
	private int _untilSway;

	/// <summary>A room whose lines are scaled by <paramref name="size"/>, 0.5 to 2.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A size outside 0.5 to 2.</exception>
	public Reverb(float size = 1f)
	{
		if (!float.IsFinite(size) || size < 0.5f || size > 2f)
			throw new ArgumentOutOfRangeException(nameof(size), size, "A room's size is 0.5 to 2.");
		_size = size;
	}

	/// <summary>How long the tail takes to fall 60 dB, in seconds, 0.1 to 20. Default 1.8, eased over 0.1 s.</summary>
	public SmoothedParameter DecaySeconds { get; } = new(1.8f, 0.1f);

	/// <summary>Where the tail starts losing its highs, in hertz: lower is a softer, more furnished room. Default 6000.</summary>
	public AudioParameter DampingHz { get; } = new(6000f);

	/// <summary>The gap before the room answers, in seconds, 0 to 0.25. Default 0.015.</summary>
	public AudioParameter PreDelaySeconds { get; } = new(0.015f);

	/// <summary>
	/// How far four of the lines sway in length, 0 (still: the same tail every time) to 1 (ten samples either way, under
	/// once a second: about two cents, too little and too slow to hear as a wobble). It spreads a held note's tail over
	/// the frequencies round the note, where a still network rings on at one or two of its own. Default 1.
	/// </summary>
	public AudioParameter Modulation { get; } = new(1f);

	/// <summary>The reverb's share of the output, a linear gain. Default 1.</summary>
	public SmoothedParameter Wet { get; } = new(1f);

	/// <summary>The input's share of the output, a linear gain. Default 0: a bus fed by sends has the dry sound elsewhere.</summary>
	public SmoothedParameter Dry { get; } = new(0f);

	/// <inheritdoc/>
	protected override void OnPrepare(int sampleRate)
	{
		var scale = sampleRate / 48000f;
		var room = (int)MathF.Ceiling(SwayFrames * scale) + 4;
		for (var i = 0; i < Lines; i++)
		{
			_lengths[i] = Math.Max(4, (int)MathF.Round(LineBase[i] * scale * _size));
			_lines[i] = new DelayLine(_lengths[i] + room);
		}
		for (var i = 0; i < AllPassBase.Length; i++)
		{
			_allPassLengths[i] = Math.Max(1, (int)MathF.Round(AllPassBase[i] * scale));
			_allPasses[i] = new DelayLine(_allPassLengths[i]);
		}
		for (var i = 0; i < SwayHz.Length; i++)
		{
			_swayLfo[i].Set(SwayHz[i] * SwayEvery, sampleRate);
			_swayLfo[i].Reset(0.25f * i);
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
		var swayDepth = Math.Clamp(Modulation.Snap(), 0f, 1f) * SwayFrames * rate / 48000f;
		Wet.Begin();
		Dry.Begin();

		var lines = _lines;
		var lengths = _lengths;
		var gains = _gains;
		var low = _low;
		var o = _out;
		var m = _mixed;
		var sway = _sway;
		for (var s = 0; s < n; s++)
		{
			// Each swaying line's length now, worked out every few samples: it moves far less than a sample in that time
			if (--_untilSway <= 0)
			{
				_untilSway = SwayEvery;
				for (var i = 0; i < sway.Length; i++)
					sway[i] = swayDepth * _swayLfo[i].Next();
			}

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

			// What comes out of each line, the odd ones at their swaying lengths, each then losing some of its highs
			for (var i = 0; i < Lines; i++)
			{
				o[i] = (i & 1) == 1 && swayDepth > 0f
					? lines[i].ReadHermite(lengths[i] + sway[i >> 1])
					: lines[i].Read(lengths[i]);
				low[i] += damp * (o[i] - low[i]);
				m[i] = low[i];
			}

			// The Hadamard mix, as three rounds of sums and differences: every line gets an equal share of all eight,
			// with signs that keep the whole from gaining or losing energy
			for (var half = 1; half < Lines; half <<= 1)
				for (var i = 0; i < Lines; i += half << 1)
					for (var j = i; j < i + half; j++)
					{
						var a = m[j];
						var b = m[j + half];
						m[j] = a + b;
						m[j + half] = a - b;
					}

			for (var i = 0; i < Lines; i++)
			{
				var v = x + m[i] * MixScale * gains[i];
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
