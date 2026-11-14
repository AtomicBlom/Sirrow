namespace Loom.Audio;

/// <summary>
/// Sound a game makes itself, sample by sample: a synthesizer, a procedural engine note, generated music. Subclass it,
/// play it with <see cref="AudioEngine.Play(AudioSource, AudioBus, float, float)"/>, and steer it from the game through
/// <see cref="AudioParameter"/> and <see cref="AudioEventQueue{T}"/> fields of its own.
/// </summary>
/// <remarks>
/// <see cref="Render"/> runs on the audio thread, every few milliseconds, against a deadline: a block late is heard as a
/// click. So it must not allocate (a garbage collection stops the audio thread with every other), lock, block, or call
/// anything that might. Work out tables and coefficients in <see cref="OnStart"/>, which runs on the game thread. A source
/// plays on one voice at a time; playing it again while it plays throws.
/// </remarks>
public abstract class AudioSource
{
	/// <summary>The engine slot playing this source, or -1. Game thread only.</summary>
	internal int VoiceSlot = -1;

	/// <summary>
	/// Whether a voice is playing this source, as the game thread last heard from the audio thread: true from its play
	/// until the engine hears the voice end (at <see cref="AudioEngine.Update"/> or the engine's next play of a
	/// source), which after a stop is once its fade has been rendered.
	/// </summary>
	public bool IsPlaying => VoiceSlot >= 0;

	/// <summary>
	/// Called on the game thread as the source starts playing, with the engine's rate: size buffers and work out
	/// rate-dependent coefficients here, not in <see cref="Render"/>.
	/// </summary>
	protected virtual void OnStart(int sampleRate) { }

	/// <summary>
	/// Writes the next block into <paramref name="left"/> and <paramref name="right"/>, which arrive zeroed and hold
	/// <see cref="AudioRenderInfo.Frames"/> samples each. Returns false once the source has finished, which ends its voice;
	/// true to keep playing. Runs on the audio thread: no allocation, locks or blocking. An exception thrown here stops the
	/// voice and is thrown again on the game thread, from the engine's next <see cref="AudioEngine.Update"/>.
	/// </summary>
	protected abstract bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info);

	internal void Start(int sampleRate) => OnStart(sampleRate);

	internal bool RenderBlock(Span<float> left, Span<float> right, in AudioRenderInfo info) => Render(left, right, in info);
}
