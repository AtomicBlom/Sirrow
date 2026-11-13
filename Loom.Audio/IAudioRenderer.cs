namespace Loom.Audio;

/// <summary>What an <see cref="IAudioDevice"/> asks for samples: the mixer, in an app.</summary>
public interface IAudioRenderer
{
	/// <summary>
	/// Fills <paramref name="output"/> with the next <c>output.Length / 2</c> frames, interleaved stereo (left, right),
	/// overwriting every sample. Called on the device's audio thread, where it must not block, allocate or throw: a stall
	/// here is heard as a click.
	/// </summary>
	void Render(Span<float> output);
}
