namespace BinaryVibrance.Sirrow;

/// <summary>
/// An output device as a platform's backend opens it: the seam between the mixer and the platform's audio API (SDL 3 on
/// the desktop, AAudio on Android, an AudioWorklet in the browser). A device owns the thread audio is rendered on and
/// asks its <see cref="IAudioRenderer"/> for samples there. It never mixes: everything audible is the renderer's.
/// </summary>
/// <remarks>
/// Samples are always interleaved stereo 32-bit float at <see cref="SampleRate"/>; a backend whose device wants another
/// layout converts. Every method but the renderer's callback is called on the thread that owns the
/// <see cref="AudioEngine"/>.
/// </remarks>
public interface IAudioDevice : IDisposable
{
	/// <summary>The backend's name: "SDL", "AAudio", "WebAudio", "Null", "Offline".</summary>
	string Backend { get; }

	/// <summary>The output's name as the platform reports it, or why there is no output (for a null device).</summary>
	string Name { get; }

	/// <summary>The rate the device takes samples at, in frames per second.</summary>
	int SampleRate { get; }

	/// <summary>
	/// The device's buffer in frames as the platform granted it: about how many frames each render asks for, and so the
	/// latency the mixer adds. 0 when the backend can't say.
	/// </summary>
	int BufferFrames { get; }

	/// <summary>
	/// How many times the output ran out of samples since the device opened, as the platform counts them (AAudio's xruns):
	/// each is a click or a gap. -1 when the platform doesn't count them.
	/// </summary>
	int Underruns => -1;

	/// <summary>Starts asking <paramref name="renderer"/> for samples, on the device's own thread. Called once.</summary>
	void Start(IAudioRenderer renderer);

	/// <summary>Stops asking for samples until <see cref="Resume"/>: the app has left the screen. The output goes silent.</summary>
	void Pause();

	/// <summary>Starts asking for samples again after <see cref="Pause"/>.</summary>
	void Resume();

	/// <summary>
	/// Called by <see cref="AudioEngine.Update"/> once a frame, on the engine's thread, with the time since the last call.
	/// A device that has no audio thread of its own (the browser's, whose mix runs on the frame's thread) renders and
	/// sends here what the output will need before the next frame; every other device does nothing.
	/// </summary>
	void Pump(double elapsedSeconds) { }
}
