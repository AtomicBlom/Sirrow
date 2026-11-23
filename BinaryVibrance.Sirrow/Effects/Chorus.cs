using BinaryVibrance.Sirrow.Dsp;

namespace BinaryVibrance.Sirrow.Effects;

/// <summary>
/// Thickens a sound into several slightly out of tune: two copies a side, each delayed by a little that slowly sways,
/// the four swaying out of step so the two sides differ.
/// </summary>
public sealed class Chorus : AudioEffect
{
	private const float BaseSeconds = 0.012f;
	private const float MaxDepthSeconds = 0.01f;
	private DelayLine _left = null!;
	private DelayLine _right = null!;
	private Lfo _left1;
	private Lfo _left2;
	private Lfo _right1;
	private Lfo _right2;

	/// <summary>How fast the copies sway, in cycles a second. Default 0.6.</summary>
	public AudioParameter RateHz { get; } = new(0.6f);

	/// <summary>How far each copy's delay sways either side of 12 ms, in seconds, up to 0.01. Default 0.003.</summary>
	public AudioParameter DepthSeconds { get; } = new(0.003f);

	/// <summary>How much of the copies is heard, 0 (the input alone) to 1 (the copies and half the input). Default 0.5.</summary>
	public SmoothedParameter Mix { get; } = new(0.5f);

	/// <inheritdoc/>
	protected override void OnPrepare(int sampleRate)
	{
		var frames = (int)MathF.Ceiling((BaseSeconds + MaxDepthSeconds) * sampleRate) + 4;
		_left = new DelayLine(frames);
		_right = new DelayLine(frames);
		_left1.Reset(0f);
		_left2.Reset(0.5f);
		_right1.Reset(0.25f);
		_right2.Reset(0.75f);
		Mix.Prepare(sampleRate);
		Mix.Snap();
	}

	/// <inheritdoc/>
	public override void Process(Span<float> left, Span<float> right)
	{
		var rate = SampleRate;
		var hz = RateHz.Snap();
		_left1.Set(hz, rate);
		_left2.Set(hz, rate);
		_right1.Set(hz, rate);
		_right2.Set(hz, rate);
		var depth = Math.Clamp(DepthSeconds.Snap(), 0f, MaxDepthSeconds) * rate;
		var centre = BaseSeconds * rate;
		var maxDelay = _left.MaxFrames - 2f;
		Mix.Begin();
		for (var s = 0; s < left.Length; s++)
		{
			var l1 = _left.ReadHermite(Math.Clamp(centre + depth * _left1.Next(), 2f, maxDelay));
			var l2 = _left.ReadHermite(Math.Clamp(centre + depth * _left2.Next(), 2f, maxDelay));
			var r1 = _right.ReadHermite(Math.Clamp(centre + depth * _right1.Next(), 2f, maxDelay));
			var r2 = _right.ReadHermite(Math.Clamp(centre + depth * _right2.Next(), 2f, maxDelay));
			_left.Write(left[s]);
			_right.Write(right[s]);
			var mix = Mix.Next();
			var keep = 1f - 0.5f * mix;
			left[s] = left[s] * keep + 0.5f * mix * (l1 + l2);
			right[s] = right[s] * keep + 0.5f * mix * (r1 + r2);
		}
	}
}
