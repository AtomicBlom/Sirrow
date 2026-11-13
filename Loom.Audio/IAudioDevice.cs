namespace Loom.Audio;

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

	/// <summary>Starts asking <paramref name="renderer"/> for samples, on the device's own thread. Called once.</summary>
	void Start(IAudioRenderer renderer);

	/// <summary>Stops asking for samples until <see cref="Resume"/>: the app has left the screen. The output goes silent.</summary>
	void Pause();

	/// <summary>Starts asking for samples again after <see cref="Pause"/>.</summary>
	void Resume();
}
