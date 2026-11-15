namespace Loom.Audio.Synth.Effects;

/// <summary>
/// Turns down what is louder than <see cref="ThresholdDb"/> by <see cref="Ratio"/>, so loud and quiet sit closer
/// together: a level above the threshold rises a quarter as fast at a ratio of 4. Both sides are turned down together,
/// by the louder, so the image stays put.
/// </summary>
public sealed class Compressor : AudioEffect
{
	private float _held;      // the amount the curve asks for, held through the troughs by the release, in decibels: zero or more
	private float _reduction; // how far the sound is turned down now, in decibels: zero or more
	private volatile float _reported;

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
	public override void Process(Span<float> left, Span<float> right)
	{
		var rate = SampleRate;
		var threshold = ThresholdDb.Snap();
		var slope = 1f / MathF.Max(1f, Ratio.Snap()) - 1f;
		var knee = MathF.Max(0f, KneeDb.Snap());
		var attack = MathF.Exp(-1f / (MathF.Max(1e-5f, AttackSeconds.Snap()) * rate));
		var release = MathF.Exp(-1f / (MathF.Max(1e-5f, ReleaseSeconds.Snap()) * rate));
		var makeup = MakeupDb.Snap();
		var held = _held;
		var reduction = _reduction;
		for (var s = 0; s < left.Length; s++)
		{
			var x = 20f * MathF.Log10(MathF.Max(MathF.Max(MathF.Abs(left[s]), MathF.Abs(right[s])), 1e-6f));
			var over = x - threshold;
			float y;
			if (2f * over < -knee)
				y = x;
			else if (knee > 0f && 2f * MathF.Abs(over) <= knee)
			{
				var into = over + knee / 2f;
				y = x + slope * into * into / (2f * knee);
			}
			else
				y = x + slope * over;
			// How far down the curve wants it, held through the troughs between a wave's peaks by the release, then eased
			// toward by the attack: smoothing the raw amount instead lets it go back up between every peak, so a steady
			// tone over the threshold would never come all the way down
			var wanted = x - y;
			held = MathF.Max(wanted, release * held + (1f - release) * wanted);
			reduction = attack * reduction + (1f - attack) * held;
			var gain = MathF.Pow(10f, (makeup - reduction) / 20f);
			left[s] *= gain;
			right[s] *= gain;
		}
		if (held < 1e-12f)
			held = 0f;
		if (reduction < 1e-12f)
			reduction = 0f;
		_held = held;
		_reduction = reduction;
		_reported = -reduction;
	}
}
