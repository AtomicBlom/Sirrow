namespace Loom.Audio;

/// <summary>
/// Something that changes the sound passing through a bus: a reverb, a limiter, a distortion. Subclass it, write
/// <see cref="Process"/>, and add it to a bus with <see cref="AudioEngine.AddEffect"/>. Its controls are
/// <see cref="AudioParameter"/> or <see cref="SmoothedParameter"/> members of its own, as an <see cref="AudioSource"/>'s are.
/// </summary>
/// <remarks>
/// <see cref="Process"/> runs on the audio thread, under the same rules as <see cref="AudioSource"/>'s render: no
/// allocation, locks or blocking. Size buffers and work out coefficients in <see cref="OnPrepare"/>, on the game thread.
/// An effect instance belongs to one bus; a synthesizer that runs effects on groups of its own calls
/// <see cref="Prepare"/> and <see cref="Process"/> itself.
/// </remarks>
public abstract class AudioEffect
{
	/// <summary>The most frames <see cref="Process"/> is ever handed at once.</summary>
	public const int MaxBlockFrames = 256;

	private volatile bool _bypass;

	/// <summary>True lets the sound past unchanged; false (the default) processes it. Any thread may set it.</summary>
	public bool Bypass
	{
		get => _bypass;
		set => _bypass = value;
	}

	/// <summary>Whether <see cref="Prepare"/> has been called.</summary>
	public bool IsPrepared { get; private set; }

	/// <summary>The rate the effect was prepared for, in frames per second: valid once <see cref="IsPrepared"/>.</summary>
	public int SampleRate { get; private set; }

	/// <summary>Set by the mixer when <see cref="Process"/> threw: the effect is skipped from then on.</summary>
	internal bool Faulted;

	/// <summary>
	/// Readies the effect for <paramref name="sampleRate"/>, calling <see cref="OnPrepare"/>. Game thread, once:
	/// <see cref="AudioEngine.AddEffect"/> calls it.
	/// </summary>
	/// <exception cref="InvalidOperationException">The effect is already prepared: it belongs to another bus.</exception>
	public void Prepare(int sampleRate)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
		if (IsPrepared)
			throw new InvalidOperationException(
				"The effect is already prepared: an effect instance belongs to one bus. Make another instance for another bus.");
		SampleRate = sampleRate;
		OnPrepare(sampleRate);
		IsPrepared = true;
	}

	/// <summary>Called once from <see cref="Prepare"/>, on the game thread: size buffers and work out coefficients here.</summary>
	protected virtual void OnPrepare(int sampleRate) { }

	/// <summary>
	/// Changes <paramref name="left"/> and <paramref name="right"/> in place: two spans of equal length, at most
	/// <see cref="MaxBlockFrames"/>. Runs on the audio thread. An exception thrown here stops the effect (the sound passes
	/// unchanged from then on) and is thrown again on the game thread, from the engine's next <see cref="AudioEngine.Update"/>.
	/// </summary>
	public abstract void Process(Span<float> left, Span<float> right);
}
