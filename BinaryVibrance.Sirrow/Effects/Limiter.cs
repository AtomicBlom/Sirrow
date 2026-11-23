using BinaryVibrance.Sirrow.Dsp;

namespace BinaryVibrance.Sirrow.Effects;

/// <summary>
/// Keeps the output under a ceiling, for the end of a mix. It delays the sound by <see cref="LatencyFrames"/> so its
/// gain can come down before a peak arrives, and clamps whatever still gets past, so nothing exceeds the ceiling.
/// </summary>
public sealed class Limiter : AudioEffect
{
	/// <summary>How far it delays the sound, in frames: how far ahead it sees a peak coming.</summary>
	public const int LatencyFrames = 64;

	private readonly DelayLine _left = new(LatencyFrames);
	private readonly DelayLine _right = new(LatencyFrames);
	private readonly float _attack = 1f - MathF.Exp(-1f / 16f);
	private float _gain = 1f;

	/// <summary>The most the output reaches, in decibels of full scale. Default −1.</summary>
	public AudioParameter CeilingDb { get; } = new(-1f);

	/// <summary>How fast the gain comes back after a peak, in seconds (a time constant). Default 0.1.</summary>
	public AudioParameter ReleaseSeconds { get; } = new(0.1f);

	/// <inheritdoc/>
	public override void Process(Span<float> left, Span<float> right)
	{
		var ceiling = MathF.Pow(10f, CeilingDb.Snap() / 20f);
		var release = 1f - MathF.Exp(-1f / (MathF.Max(1e-5f, ReleaseSeconds.Snap()) * SampleRate));
		var g = _gain;
		for (var s = 0; s < left.Length; s++)
		{
			var peak = MathF.Max(MathF.Abs(left[s]), MathF.Abs(right[s]));
			var target = peak > ceiling ? ceiling / peak : 1f;
			g += (target - g) * (target < g ? _attack : release);
			var outL = Math.Clamp(_left.Read(LatencyFrames) * g, -ceiling, ceiling);
			var outR = Math.Clamp(_right.Read(LatencyFrames) * g, -ceiling, ceiling);
			_left.Write(left[s]);
			_right.Write(right[s]);
			left[s] = outL;
			right[s] = outR;
		}
		_gain = g;
	}
}
