namespace Loom.Audio;

/// <summary>
/// Sound decoded into memory at its engine's rate, ready to play any number of times at once: a footstep, a hit, a loop.
/// Made by <see cref="AudioEngine.CreateClip"/> or <see cref="AudioEngine.LoadWav"/>, which convert it to the engine's
/// rate as it loads so playing it never resamples (a pitch other than 1 aside).
/// </summary>
public sealed class AudioClip
{
	internal AudioClip(float[] samples, int channels, int sampleRate)
	{
		Samples = samples;
		Channels = channels;
		SampleRate = sampleRate;
		Frames = samples.Length / channels;
	}

	/// <summary>The samples, interleaved when stereo.</summary>
	internal float[] Samples { get; }

	/// <summary>1 for mono, 2 for stereo.</summary>
	public int Channels { get; }

	/// <summary>How many frames the clip holds: samples per channel.</summary>
	public int Frames { get; }

	/// <summary>The rate the clip's samples are at: its engine's.</summary>
	public int SampleRate { get; }

	/// <summary>How long the clip plays at a pitch of 1, in seconds.</summary>
	public double DurationSeconds => (double)Frames / SampleRate;
}
