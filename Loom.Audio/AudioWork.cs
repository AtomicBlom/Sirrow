namespace Loom.Audio;

/// <summary>
/// Where the engine's work off the audio thread runs: decoding streams ahead (<see cref="AudioStream"/>) and rendering
/// sources ahead (<see cref="AheadSource"/>).
/// </summary>
public enum AudioWork
{
	/// <summary>A thread of the engine's own, made when first needed (or at once, with <see cref="AudioOptions.StartWorker"/>).</summary>
	Thread,

	/// <summary>
	/// <see cref="AudioEngine.Update"/>, on the thread calling it: deterministic, for tests and offline renders, and the
	/// browser's choice unless <see cref="AudioOptions.BrowserThread"/>.
	/// </summary>
	Update,
}
