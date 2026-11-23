namespace BinaryVibrance.Sirrow.Mixing;

/// <summary>Turns decoded samples into an <see cref="AudioClip"/> at the engine's rate.</summary>
internal static class ClipFactory
{
	/// <summary>
	/// A clip of <paramref name="samples"/> (interleaved when stereo) at <paramref name="sourceRate"/>, converted to
	/// <paramref name="engineRate"/>. The conversion is a 4-point Hermite: lowering the rate doesn't filter what lies above
	/// the new limit, so a 96 kHz source loaded at 48 kHz can alias faintly. Supply it at the engine's rate where that matters.
	/// </summary>
	public static AudioClip Create(ReadOnlySpan<float> samples, int channels, int sourceRate, int engineRate)
	{
		if (channels is not (1 or 2))
			throw new ArgumentOutOfRangeException(nameof(channels), channels, "A clip is mono or stereo.");
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceRate);
		if (samples.Length % channels != 0)
			throw new ArgumentException($"{samples.Length} samples is not a whole number of {channels}-channel frames.", nameof(samples));

		if (sourceRate == engineRate)
			return new AudioClip(samples.ToArray(), channels, engineRate);

		var source = samples.ToArray();
		var inFrames = source.Length / channels;
		var outFrames = (int)Math.Round(inFrames * (double)engineRate / sourceRate);
		var output = new float[outFrames * channels];
		var step = (double)sourceRate / engineRate;
		for (var f = 0; f < outFrames; f++)
		{
			var position = f * step;
			var index = (int)position;
			var t = (float)(position - index);
			for (var c = 0; c < channels; c++)
			{
				output[f * channels + c] = Interpolation.Hermite(
					Interpolation.At(source, channels, c, index - 1, inFrames, loop: false),
					Interpolation.At(source, channels, c, index, inFrames, loop: false),
					Interpolation.At(source, channels, c, index + 1, inFrames, loop: false),
					Interpolation.At(source, channels, c, index + 2, inFrames, loop: false),
					t);
			}
		}
		return new AudioClip(output, channels, engineRate);
	}
}
