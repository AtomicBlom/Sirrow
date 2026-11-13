namespace Loom.Audio;

/// <summary>
/// A playing voice: a clip or a source started by <see cref="AudioEngine.Play(AudioClip, AudioBus, float, float, float, bool)"/>.
/// Use it to change the voice's volume, pitch or pan, or to stop it. A handle goes stale once its voice ends, and the
/// engine ignores a stale one rather than reach the voice that took its place, so a game can keep handles without
/// tracking when each sound finishes.
/// </summary>
/// <param name="Slot">The voice's place in the engine's pool.</param>
/// <param name="Generation">Which use of that place: a reused slot gets a new one, which is what makes an old handle stale.</param>
public readonly record struct VoiceHandle(int Slot, int Generation)
{
	/// <summary>No voice: what a play returns when every voice is busy.</summary>
	public static VoiceHandle None => default;

	/// <summary>Whether this is <see cref="None"/>.</summary>
	public bool IsNone => Generation == 0;
}
