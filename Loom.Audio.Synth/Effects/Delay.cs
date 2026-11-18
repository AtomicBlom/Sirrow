using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Effects;

/// <summary>
/// Echoes: the sound again after <see cref="TimeSeconds"/>, each repeat quieter and darker. Ping-pong bounces the
/// repeats between the sides. A change of time glides, bending the echoes' pitch as a tape delay's would.
/// </summary>
public sealed class Delay : AudioEffect
{
	private readonly bool _pingPong;
	private readonly float _maxSeconds;
	private DelayLine _left = null!;
	private DelayLine _right = null!;
	private OnePole _lowLeft;
	private OnePole _lowRight;

	/// <summary>A delay of up to <paramref name="maxSeconds"/>, its repeats bouncing between the sides when <paramref name="pingPong"/>.</summary>
	public Delay(bool pingPong = false, float maxSeconds = 2f)
	{
		if (!float.IsFinite(maxSeconds) || maxSeconds <= 0f)
			throw new ArgumentOutOfRangeException(nameof(maxSeconds), maxSeconds, "A delay's longest time is more than zero seconds.");
		_pingPong = pingPong;
		_maxSeconds = maxSeconds;
	}

	/// <summary>The time to the first repeat, in seconds. Default 0.375, eased over 0.2 s. <see cref="Beats"/> gives it in time with music.</summary>
	public SmoothedParameter TimeSeconds { get; } = new(0.375f, 0.2f);

	/// <summary>How much of each repeat comes round again, 0 to 0.95. Default 0.35.</summary>
	public AudioParameter Feedback { get; } = new(0.35f);

	/// <summary>Where the repeats lose their highs, in hertz. Default 4000.</summary>
	public AudioParameter DampingHz { get; } = new(4000f);

	/// <summary>The repeats' share of the output, a linear gain. Default 0.5.</summary>
	public SmoothedParameter Wet { get; } = new(0.5f);

	/// <summary>The input's share of the output, a linear gain. Default 1.</summary>
	public SmoothedParameter Dry { get; } = new(1f);

	/// <summary>The length of <paramref name="beats"/> beats at <paramref name="bpm"/> beats a minute, in seconds.</summary>
	public static float Beats(float beats, float bpm) => beats * 60f / bpm;

	/// <summary>An echo can be as far off as the delay's longest time, so it has nothing left only after a silence longer than that.</summary>
	protected override double MemorySeconds => _maxSeconds + 0.1;

	/// <inheritdoc/>
	protected override void OnPrepare(int sampleRate)
	{
		var frames = (int)MathF.Ceiling(_maxSeconds * sampleRate) + 4;
		_left = new DelayLine(frames);
		_right = new DelayLine(frames);
		TimeSeconds.Prepare(sampleRate);
		Wet.Prepare(sampleRate);
		Dry.Prepare(sampleRate);
		TimeSeconds.Snap();
		Wet.Snap();
		Dry.Snap();
	}

	/// <inheritdoc/>
	public override void Process(Span<float> left, Span<float> right)
	{
		var rate = SampleRate;
		var feedback = Math.Clamp(Feedback.Snap(), 0f, 0.95f);
		var damping = DampingHz.Snap();
		_lowLeft.Set(damping, rate);
		_lowRight.Set(damping, rate);
		var maxDelay = _left.MaxFrames - 2f;
		TimeSeconds.Begin();
		Wet.Begin();
		Dry.Begin();
		for (var s = 0; s < left.Length; s++)
		{
			var d = Math.Clamp(TimeSeconds.Next() * rate, 2f, maxDelay);
			var readL = _left.ReadHermite(d);
			var readR = _right.ReadHermite(d);
			var fbL = _lowLeft.LowPass(readL) * feedback;
			var fbR = _lowRight.LowPass(readR) * feedback;
			float writeL, writeR;
			if (_pingPong)
			{
				writeL = 0.5f * (left[s] + right[s]) + fbR;
				writeR = fbL;
			}
			else
			{
				writeL = left[s] + fbL;
				writeR = right[s] + fbR;
			}
			Denormal.Flush(ref writeL);
			Denormal.Flush(ref writeR);
			_left.Write(writeL);
			_right.Write(writeR);
			var wet = Wet.Next();
			var dry = Dry.Next();
			left[s] = dry * left[s] + wet * readL;
			right[s] = dry * right[s] + wet * readR;
		}
		_lowLeft.Flush();
		_lowRight.Flush();
	}
}
