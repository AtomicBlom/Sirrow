namespace BinaryVibrance.Sirrow;

/// <summary>
/// The platform's audio output, as the app's own code opens it: the devices an app has registered, tried in the order
/// they were registered. Each platform's device comes in a package of its own, which its entry point registers once
/// before anything opens audio:
/// <code>
/// SdlAudio.Register();        // the desktop (BinaryVibrance.Sirrow.Sdl)
/// AAudioOutput.Register();    // Android (BinaryVibrance.Sirrow.Android)
/// WebAudio.Register();        // the browser (BinaryVibrance.Sirrow.Web)
///
/// var options = new AudioOptions();
/// var audio = new AudioEngine(AudioOutput.Open(options), options);
/// </code>
/// Shared code then opens audio the same way on every platform. Registering is explicit, not done by a device's package
/// on its own when it loads, because an assembly nothing names may never load, and NativeAOT and the browser's AOT keep
/// only what is reached.
/// </summary>
public static class AudioOutput
{
	private static readonly Lock Gate = new();
	private static readonly List<(string Name, Func<AudioOptions, (IAudioDevice? Device, string Reason)> Open)> Registered = [];

	/// <summary>
	/// Adds a platform's device. <paramref name="open"/> returns the device opened for the options, or null with the reason
	/// it couldn't open (its library missing, no output). Registering the same <paramref name="name"/> again does nothing.
	/// </summary>
	public static void Register(string name, Func<AudioOptions, (IAudioDevice? Device, string Reason)> open)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(open);
		lock (Gate)
		{
			if (Registered.Exists(r => r.Name == name))
				return;
			Registered.Add((name, open));
		}
	}

	/// <summary>The names of the devices registered, in the order they are tried.</summary>
	public static IReadOnlyList<string> Names
	{
		get
		{
			lock (Gate)
				return [.. Registered.Select(r => r.Name)];
		}
	}

	/// <summary>
	/// Opens the first registered device that opens for <paramref name="options"/>. Where none does, or none is
	/// registered, returns a <see cref="NullAudioDevice"/> naming why, so the app runs on, silent.
	/// </summary>
	public static IAudioDevice Open(AudioOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		(string Name, Func<AudioOptions, (IAudioDevice? Device, string Reason)> Open)[] registered;
		lock (Gate)
			registered = [.. Registered];
		var rate = options.SampleRate > 0 ? options.SampleRate : 48_000;
		if (registered.Length == 0)
			return new NullAudioDevice(rate, "no audio output is registered (AudioOutput.Register)");
		var reasons = new List<string>(registered.Length);
		foreach (var (name, open) in registered)
		{
			var (device, reason) = open(options);
			if (device is not null)
				return device;
			reasons.Add($"{name}: {reason}");
		}
		return new NullAudioDevice(rate, string.Join("; ", reasons));
	}
}
