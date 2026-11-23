namespace BinaryVibrance.Sirrow.Sdl;

/// <summary>The desktop's audio output, through SDL 3: what an app's desktop entry point registers.</summary>
public static class SdlAudio
{
	/// <summary>
	/// Adds SDL 3's output to <see cref="AudioOutput"/>: the system's default playback device, at its own rate and the
	/// smallest buffer the platform takes unless the options ask otherwise. Where SDL3.dll is missing or no device opens,
	/// <see cref="AudioOutput.Open"/> goes on to the next registered output, or plays silent.
	/// </summary>
	public static void Register() =>
		AudioOutput.Register("SDL", options => (SdlAudioDevice.TryOpen(options, out var reason), reason));
}
