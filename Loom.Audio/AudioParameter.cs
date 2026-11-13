namespace Loom.Audio;

/// <summary>
/// A continuous control the game sets and an <see cref="AudioSource"/> reads, without a lock: a synthesizer's cutoff, an
/// engine note's RPM, a music layer's intensity. The game sets <see cref="Value"/> whenever it likes; the source takes
/// a <see cref="Ramp"/> once a block and steps through it sample by sample, so a change glides across the block instead
/// of jumping, which would click.
/// </summary>
/// <remarks>
/// A float is written whole on every platform, so the audio thread always reads a value the game set, never half of
/// one. Only the latest value matters: for changes that must all arrive, in order, use an
/// <see cref="AudioEventQueue{T}"/>.
/// </remarks>
/// <param name="initial">The value before the game sets one.</param>
public sealed class AudioParameter(float initial)
{
	private float _target = initial;
	private float _current = initial;

	/// <summary>The value to move to. Set it from the game; the source reaches it over its next block.</summary>
	public float Value
	{
		get => Volatile.Read(ref _target);
		set => Volatile.Write(ref _target, value);
	}

	/// <summary>Audio thread: where the parameter is now, the start of the next ramp.</summary>
	public float Current => _current;

	/// <summary>
	/// Audio thread: a straight line from where the parameter is to <see cref="Value"/> across <paramref name="frames"/>
	/// samples, which leaves the parameter at <see cref="Value"/>. Take one per block, for the block's frame count.
	/// </summary>
	public ParameterRamp Ramp(int frames)
	{
		var target = Volatile.Read(ref _target);
		var start = _current;
		_current = target;
		return new ParameterRamp(start, frames > 0 ? (target - start) / frames : 0f);
	}

	/// <summary>Audio thread: jumps straight to <see cref="Value"/>, for a control where a step is what's wanted (a waveform choice).</summary>
	public float Snap() => _current = Volatile.Read(ref _target);
}
