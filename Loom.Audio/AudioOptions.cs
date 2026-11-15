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

	/// <summary>How many ducks (<see cref="AudioEngine.AddDuck(AudioBus, AudioBus, float, float, float, float, float)"/>) the engine can hold. Default 8.</summary>
	public int MaxDucks { get; set; } = 8;

	/// <summary>How many effects (<see cref="AudioEngine.AddEffect"/>) one bus can hold. Default 8.</summary>
	public int MaxEffectsPerBus { get; set; } = 8;

	/// <summary>How many sends (<see cref="AudioEngine.AddSend(AudioBus, AudioBus, float)"/>) the engine can hold. Default 16.</summary>
	public int MaxSends { get; set; } = 16;

	/// <summary>
	/// The output's volume while the app is out of focus (another window in front, the browser tab hidden): a linear gain
	/// the whole mix fades to, then back from on return, over a quarter of a second. 1 (the default) keeps playing as is; 0
	/// silences the app. The volumes the game sets, the master bus's included, are left alone.
	/// </summary>
	public float UnfocusedVolume { get; set; } = 1f;

	/// <summary>
	/// The speed of sound in the game's units a second, for the Doppler shift of 3D voices. Default 343, for a game whose
	/// unit is the metre: set it to 1125 for feet, or to whatever makes the game's shifts feel right.
	/// </summary>
	public float SpeedOfSound { get; set; } = 343f;

	/// <summary>
	/// How many commands (plays, stops, volume changes) the game can have waiting for the audio thread at once. The audio
	/// thread takes them every few milliseconds, so the default of 4096 is only reached by a game sending thousands in
	/// one frame; past it the engine throws rather than drop one.
	/// </summary>
	public int CommandCapacity { get; set; } = 4096;
}
