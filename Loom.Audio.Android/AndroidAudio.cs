namespace Loom.Audio.Android;

/// <summary>Android's audio output, through AAudio: what an app's Android entry point registers.</summary>
public static class AndroidAudio
{
	/// <summary>
	/// Adds AAudio's output to <see cref="AudioOutput"/>: a low-latency stream on the default output, asking for exclusive
	/// use of it, reopened on the new default when the output goes. Where no output opens,
	/// <see cref="AudioOutput.Open"/> goes on to the next registered output, or plays silent.
	/// </summary>
	public static void Register() =>
		AudioOutput.Register("AAudio", options => (AAudioDevice.TryOpen(options, out var reason), reason));
}
