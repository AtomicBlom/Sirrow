namespace Loom.Audio;

/// <summary>
/// How an app's audio is opened and sized. Set an instance on <c>AppOptions.Audio</c> in <c>Configure</c> to have the
/// host open its platform's output device (or hand one to <see cref="AudioEngine"/> directly). Every value is a request
/// the backend meets as closely as the platform allows: <see cref="AudioEngine.Device"/> says what was granted.
/// </summary>
public sealed class AudioOptions
{
	/// <summary>
	/// The mixing rate in frames per second, or 0 (the default) for the output device's own rate. The device's own rate
	/// is the low-latency choice: any other makes the platform resample, which adds latency of its own.
	/// </summary>
	public int SampleRate { get; set; }

	/// <summary>
	/// The device buffer in frames, or 0 (the default) for the smallest the platform's backend offers. The buffer is the
	/// latency the mixer adds: 128 frames is 2.7 ms at 48 kHz. Raise it only if the output crackles on a slow device.
	/// </summary>
	public int BufferFrames { get; set; }

	/// <summary>
	/// How many voices can play at once: clips and sources together. A play past it is dropped and returns
	/// <see cref="VoiceHandle.None"/> (counted in <see cref="AudioStats.DroppedVoices"/>). Default 64.
	/// </summary>
	public int MaxVoices { get; set; } = 64;

	/// <summary>How many buses the engine can hold, the master bus included. Default 16.</summary>
	public int MaxBuses { get; set; } = 16;

	/// <summary>
	/// How many commands (plays, stops, volume changes) the game can have waiting for the audio thread at once. The audio
	/// thread takes them every few milliseconds, so the default of 4096 is only reached by a game sending thousands in
	/// one frame; past it the engine throws rather than drop one.
	/// </summary>
	public int CommandCapacity { get; set; } = 4096;
}
