namespace Loom.Audio.Synth.Chip;

/// <summary>A recording for a <see cref="OperatorKind.Sample"/> operator: mono samples at their own rate.</summary>
/// <param name="Samples">The samples, -1..1.</param>
/// <param name="SampleRate">Their rate, in frames per second.</param>
public sealed record SampleData(float[] Samples, int SampleRate)
{
	/// <summary>Decoded audio as a sample: stereo averaged to mono.</summary>
	public static SampleData From(AudioData data)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Channels == 1)
			return new SampleData(data.Samples, data.SampleRate);
		var mono = new float[data.Frames];
		for (var i = 0; i < mono.Length; i++)
			mono[i] = 0.5f * (data.Samples[2 * i] + data.Samples[2 * i + 1]);
		return new SampleData(mono, data.SampleRate);
	}
}
