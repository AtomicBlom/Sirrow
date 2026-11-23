using BinaryVibrance.Sirrow.Dsp;

namespace BinaryVibrance.Sirrow.Effects;

/// <summary>
/// Drives the sound into a curve that bends its peaks (<see cref="DistortionCurve"/>), oversampled so the harmonics it
/// makes above half the rate don't fold back down as harsh, unrelated tones; then a tone control and, if chosen, a
/// speaker cabinet's response. The dry sound is delayed to line up with the oversampler's own delay, so a mix doesn't comb.
/// </summary>
public sealed class Distortion : AudioEffect
{
	private readonly DistortionCurve _curve;
	private readonly Cabinet _cabinet;
	private readonly int _factor;
	private readonly int _dryDelay;
	private readonly Channel _left;
	private readonly Channel _right;
	private readonly float[] _drive = new float[MaxBlockFrames];
	private readonly float[] _output = new float[MaxBlockFrames];
	private readonly float[] _mix = new float[MaxBlockFrames];

	/// <summary>
	/// A distortion through <paramref name="curve"/>, heard through <paramref name="cabinet"/>, oversampled
	/// <paramref name="oversampling"/> times (1, 2 or 4: more is cleaner and costs more).
	/// </summary>
	public Distortion(DistortionCurve curve = DistortionCurve.Soft, Cabinet cabinet = Cabinet.None, int oversampling = 4)
	{
		if (oversampling is not (1 or 2 or 4))
			throw new ArgumentOutOfRangeException(nameof(oversampling), oversampling, "A distortion oversamples 1, 2 or 4 times.");
		if (curve is < DistortionCurve.Soft or > DistortionCurve.Fold)
			throw new ArgumentOutOfRangeException(nameof(curve), curve, "No such curve.");
		if (cabinet is < Cabinet.None or > Cabinet.FourByTwelve)
			throw new ArgumentOutOfRangeException(nameof(cabinet), cabinet, "No such cabinet.");
		_curve = curve;
		_cabinet = cabinet;
		_factor = oversampling;
		_left = new Channel(oversampling);
		_right = new Channel(oversampling);
		_dryDelay = _left.Latency;
	}

	/// <summary>How hard the sound is pushed into the curve, in decibels. Default 12, eased.</summary>
	public SmoothedParameter DriveDb { get; } = new(12f);

	/// <summary>The tone control: a low-pass after the curve, in hertz. Default 4000.</summary>
	public AudioParameter ToneHz { get; } = new(4000f);

	/// <summary>The distorted sound's level, in decibels. Default −6, eased.</summary>
	public SmoothedParameter OutputDb { get; } = new(-6f);

	/// <summary>The distorted sound's share, 0 (dry) to 1 (all distorted). Default 1, eased.</summary>
	public SmoothedParameter Mix { get; } = new(1f);

	/// <summary>How far the dry sound is delayed to line up with the oversampler, in frames: 47, 31 or 0.</summary>
	public int DryDelayFrames => _dryDelay;

	/// <inheritdoc/>
	protected override void OnPrepare(int sampleRate)
	{
		_left.Prepare(_cabinet, sampleRate);
		_right.Prepare(_cabinet, sampleRate);
		DriveDb.Prepare(sampleRate);
		OutputDb.Prepare(sampleRate);
		Mix.Prepare(sampleRate);
		DriveDb.Snap();
		OutputDb.Snap();
		Mix.Snap();
	}

	/// <inheritdoc/>
	public override void Process(Span<float> left, Span<float> right)
	{
		var n = left.Length;
		DriveDb.Begin();
		OutputDb.Begin();
		Mix.Begin();
		// The eased values are shared by both sides, so they are stepped once here for the block; one that has arrived is
		// the same every sample, and its decibels are turned to a gain once
		Gains(DriveDb, _drive, n);
		Gains(OutputDb, _output, n);
		if (Mix.IsSettled)
			_mix.AsSpan(0, n).Fill(Mix.Skip(n));
		else
			for (var s = 0; s < n; s++)
				_mix[s] = Mix.Next();
		var tone = ToneHz.Snap();
		_left.Run(left, this, tone);
		_right.Run(right, this, tone);
	}

	// A parameter in decibels as the gain of each of the block's samples
	private static void Gains(SmoothedParameter db, float[] into, int n)
	{
		if (db.IsSettled)
		{
			into.AsSpan(0, n).Fill(Units.DbToGain(db.Skip(n)));
			return;
		}
		for (var s = 0; s < n; s++)
			into[s] = Units.DbToGain(db.Next());
	}

	// The curve over a block of samples, chosen once for the block rather than once a sample
	private void Shape(Span<float> samples)
	{
		switch (_curve)
		{
			case DistortionCurve.Soft:
				for (var i = 0; i < samples.Length; i++)
					samples[i] = MathF.Tanh(samples[i]);
				break;
			case DistortionCurve.Hard:
				for (var i = 0; i < samples.Length; i++)
				{
					var x = samples[i];
					samples[i] = x <= -1f ? -1f : x >= 1f ? 1f : 1.5f * x - 0.5f * x * x * x;
				}
				break;
			case DistortionCurve.Asymmetric:
				for (var i = 0; i < samples.Length; i++)
					samples[i] = MathF.Tanh(samples[i] + 0.2f) - 0.19737533f; // tanh(0.2), so silence stays silent
				break;
			default:
				for (var i = 0; i < samples.Length; i++)
					samples[i] = Fold(samples[i]);
				break;
		}
	}

	private static float Fold(float x)
	{
		var t = x / 4f + 0.25f;
		return 1f - 4f * MathF.Abs(t - MathF.Floor(t) - 0.5f);
	}

	// One side's state: the dry path's delay, the filters and oversampler, and its scratch buffers
	private sealed class Channel
	{
		private readonly Oversampler? _oversampler;
		private readonly DelayLine? _dryLine;
		private readonly int _dryDelay;
		private readonly float[] _dry = new float[MaxBlockFrames];
		private readonly float[] _wet = new float[MaxBlockFrames];
		private readonly float[] _raised;
		private readonly Biquad[] _cabinet = new Biquad[4];
		private int _cabinetCount;
		private OnePole _highPass;
		private OnePole _tone;
		private int _rate;

		public Channel(int factor)
		{
			_oversampler = factor > 1 ? new Oversampler(factor, MaxBlockFrames) : null;
			_dryDelay = _oversampler?.LatencyFrames ?? 0;
			_dryLine = _dryDelay > 0 ? new DelayLine(_dryDelay) : null;
			_raised = new float[MaxBlockFrames * factor];
		}

		// How far the oversampler delays the wet path, and so the dry
		public int Latency => _dryDelay;

		public void Prepare(Cabinet cabinet, int rate)
		{
			_rate = rate;
			_highPass.Set(80f, rate);
			switch (cabinet)
			{
				case Cabinet.OneByTwelve:
					_cabinet[0].SetHighPass(90f, 0.7f, rate);
					_cabinet[1].SetPeak(2200f, 1.2f, 4f, rate);
					_cabinet[2].SetLowPass(5500f, 0.7f, rate);
					_cabinetCount = 3;
					break;
				case Cabinet.FourByTwelve:
					_cabinet[0].SetHighPass(75f, 0.7f, rate);
					_cabinet[1].SetPeak(120f, 1f, 3f, rate);
					_cabinet[2].SetPeak(2500f, 1.5f, 4f, rate);
					_cabinet[3].SetLowPass(4500f, 0.8f, rate);
					_cabinetCount = 4;
					break;
				default:
					_cabinetCount = 0;
					break;
			}
		}

		public void Run(Span<float> samples, Distortion owner, float toneHz)
		{
			var n = samples.Length;
			var dry = _dry.AsSpan(0, n);
			var wet = _wet.AsSpan(0, n);
			for (var s = 0; s < n; s++)
			{
				var x = samples[s];
				if (_dryLine is { } line)
				{
					dry[s] = line.Read(_dryDelay);
					line.Write(x);
				}
				else
					dry[s] = x;
				wet[s] = _highPass.HighPass(x) * owner._drive[s];
			}

			if (_oversampler is { } oversampler)
			{
				var raised = _raised.AsSpan(0, n * oversampler.Factor);
				oversampler.Up(wet, raised);
				owner.Shape(raised);
				oversampler.Down(raised, wet);
			}
			else
				owner.Shape(wet);

			_tone.Set(toneHz, _rate);
			for (var s = 0; s < n; s++)
			{
				var y = _tone.LowPass(wet[s]);
				for (var c = 0; c < _cabinetCount; c++)
					y = _cabinet[c].Tick(y);
				var mix = owner._mix[s];
				samples[s] = dry[s] * (1f - mix) + y * owner._output[s] * mix;
			}

			_highPass.Flush();
			_tone.Flush();
			for (var c = 0; c < _cabinetCount; c++)
				_cabinet[c].Flush();
		}
	}
}
