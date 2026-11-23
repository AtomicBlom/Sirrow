namespace BinaryVibrance.Sirrow.Effects;

/// <summary>
/// Turns down what is louder than <see cref="ThresholdDb"/> by <see cref="Ratio"/>, so loud and quiet sit closer
/// together: a level above the threshold rises a quarter as fast at a ratio of 4. Both sides are turned down together,
/// by the louder, so the image stays put. Keyed (<see cref="Keyed"/>), it listens to a key (<see cref="IKeyedEffect"/>) instead of to what it
/// turns down: a sidechain, the bass ducking under the kick.
/// </summary>
public sealed class Compressor : AudioEffect, IKeyedEffect
{
	private readonly float[] _keyGain = new float[MaxBlockFrames];
	private int _keyFrames = -1; // how many frames of _keyGain the key gave for the block about to be run; -1 none
	private float _held;      // the amount the curve asks for, held through the troughs by the release, in decibels: zero or more
	private float _reduction; // how far the sound is turned down now, in decibels: zero or more
	private volatile float _reported;

	/// <summary>A compressor that listens to what it turns down or, <paramref name="keyed"/>, to the key.</summary>
	public Compressor(bool keyed = false) => Keyed = keyed;

	/// <summary>
	/// Whether it listens to the key (<see cref="IKeyedEffect"/>) rather than to what passes through it. A keyed
	/// compressor runs only where something hands it the key (Lunelle's SynthChip, on a group, send or master), and throws
	/// from <see cref="Process"/> anywhere else, rather than quietly doing nothing.
	/// </summary>
	public bool Keyed { get; }

	/// <summary>The level above which it turns the sound down, in decibels of full scale. Default −18.</summary>
	public AudioParameter ThresholdDb { get; } = new(-18f);

	/// <summary>How many decibels in above the threshold make one out: 1 does nothing, 20 is nearly a wall. Default 4.</summary>
	public AudioParameter Ratio { get; } = new(4f);

	/// <summary>The width, in decibels, of the soft bend round the threshold; 0 bends sharply. Default 6.</summary>
	public AudioParameter KneeDb { get; } = new(6f);

	/// <summary>How fast it turns down, in seconds (a time constant). Default 0.01.</summary>
	public AudioParameter AttackSeconds { get; } = new(0.01f);

	/// <summary>How fast it lets go, in seconds (a time constant). Default 0.15.</summary>
	public AudioParameter ReleaseSeconds { get; } = new(0.15f);

	/// <summary>A gain after it, in decibels, to win back the level it took. Default 0.</summary>
	public AudioParameter MakeupDb { get; } = new(0f);

	/// <summary>How far it had the sound turned down at the end of the last block, in decibels (zero or below). Any thread.</summary>
	public float GainReductionDb => _reported;

	/// <inheritdoc/>
	public void Key(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
	{
		if (!Keyed)
			return;
		var curve = new Curve(this);
		for (var s = 0; s < left.Length; s++)
			_keyGain[s] = Follow(MathF.Max(MathF.Abs(left[s]), MathF.Abs(right[s])), in curve);
		_keyFrames = left.Length;
		Settle();
	}

	/// <inheritdoc/>
	public override void Process(Span<float> left, Span<float> right)
	{
		if (Keyed)
		{
			if (_keyFrames != left.Length)
				throw new InvalidOperationException(
					"A keyed compressor is handed the key before each block, and this one wasn't: put it where a source hands it the key (Lunelle's SynthChip: a group, send or master).");
			for (var s = 0; s < left.Length; s++)
			{
				left[s] *= _keyGain[s];
				right[s] *= _keyGain[s];
			}
			_keyFrames = -1;
			return;
		}
		var curve = new Curve(this);
		for (var s = 0; s < left.Length; s++)
		{
			var gain = Follow(MathF.Max(MathF.Abs(left[s]), MathF.Abs(right[s])), in curve);
			left[s] *= gain;
			right[s] *= gain;
		}
		Settle();
	}

	// One sample's level through the curve and the envelope, to the gain it gets
	private float Follow(float level, in Curve curve)
	{
		// At rest and under the knee: the makeup gain, worked out once a block, without the log and the power the rest takes
		if (_held == 0f && _reduction == 0f && level < curve.QuietBelow)
			return curve.Quiet;

		var x = 20f * MathF.Log10(MathF.Max(level, 1e-6f));
		var over = x - curve.Threshold;
		float y;
		if (2f * over < -curve.Knee)
			y = x;
		else if (curve.Knee > 0f && 2f * MathF.Abs(over) <= curve.Knee)
		{
			var into = over + curve.Knee / 2f;
			y = x + curve.Slope * into * into / (2f * curve.Knee);
		}
		else
			y = x + curve.Slope * over;
		// How far down the curve wants it, held through the troughs between a wave's peaks by the release, then eased
		// toward by the attack: smoothing the raw amount instead lets it go back up between every peak, so a steady
		// tone over the threshold would never come all the way down
		var wanted = x - y;
		_held = MathF.Max(wanted, curve.Release * _held + (1f - curve.Release) * wanted);
		_reduction = curve.Attack * _reduction + (1f - curve.Attack) * _held;
		return MathF.Pow(10f, (curve.Makeup - _reduction) / 20f);
	}

	// The envelope's state flushed to zero once it is all but gone, and reported
	private void Settle()
	{
		if (_held < 1e-12f)
			_held = 0f;
		if (_reduction < 1e-12f)
			_reduction = 0f;
		_reported = -_reduction;
	}

	// The parameters as a block uses them
	private readonly struct Curve
	{
		public readonly float Threshold, Slope, Knee, Attack, Release, Makeup;

		// At rest (nothing held, nothing taken off) a level under the knee's lower edge gets exactly the makeup gain: the
		// curve leaves it as it is, so what it wants taken off is 0 and the envelope stays at 0. The edge is taken a little
		// low, in linear terms, so a level the full path would put on it never takes this one
		public readonly float QuietBelow, Quiet;

		public Curve(Compressor c)
		{
			var rate = c.SampleRate;
			Threshold = c.ThresholdDb.Snap();
			Slope = 1f / MathF.Max(1f, c.Ratio.Snap()) - 1f;
			Knee = MathF.Max(0f, c.KneeDb.Snap());
			Attack = MathF.Exp(-1f / (MathF.Max(1e-5f, c.AttackSeconds.Snap()) * rate));
			Release = MathF.Exp(-1f / (MathF.Max(1e-5f, c.ReleaseSeconds.Snap()) * rate));
			Makeup = c.MakeupDb.Snap();
			var edge = Threshold - Knee / 2f;
			// The full path floors a level at 120 dB down, so an edge below that is never quiet
			QuietBelow = edge > -119f ? 0.999f * MathF.Pow(10f, edge / 20f) : 0f;
			Quiet = MathF.Pow(10f, (Makeup - 0f) / 20f);
		}
	}
}
