using System.Runtime.CompilerServices;

namespace Loom.Audio;

/// <summary>
/// A continuous control the game sets and an <see cref="AudioSource"/> reads, eased toward its target with a time
/// constant rather than in a straight line across one block: an engine note's RPM, a tyre's squeal, a gain the game
/// sets each step. Use it for a value the game moves often and in steps (60 times a second), which an
/// <see cref="AudioParameter"/> would follow as a staircase of short ramps; use <see cref="AudioParameter"/> where the
/// game's value should be reached exactly within the block (a level set by a menu).
/// </summary>
/// <remarks>
/// The game sets <see cref="Value"/>; the source calls <see cref="Prepare"/> from its <c>OnStart</c>, <see cref="Begin"/>
/// once a block and <see cref="Next"/> once a sample, or <see cref="Skip"/> for a value it reads once a block. A float is
/// written whole on every platform, so the audio thread always reads a value the game set. Only the latest value
/// matters: for changes that must all arrive, in order, use an <see cref="AudioEventQueue{T}"/>.
/// </remarks>
/// <param name="initial">The value before the game sets one, and where the parameter starts.</param>
/// <param name="seconds">
/// The time constant: after this long the parameter has covered 63% of the way to a new target. Zero or less steps
/// straight to it.
/// </param>
public sealed class SmoothedParameter(float initial, float seconds = 0.03f)
{
	// The distance left to the target is kept rather than the value itself: a step near the target (a hundred-thousandth
	// of the way, at a 0.1 s constant) is smaller than a float can resolve around the value, and would stall short of it,
	// while the distance keeps its full precision however small it gets.
	private float _value = initial;
	private float _current = initial;
	private float _target = initial;
	private float _error;
	private float _keep;

	/// <summary>The value to move to. Set it from the game; the source eases toward it.</summary>
	public float Value
	{
		get => Volatile.Read(ref _value);
		set => Volatile.Write(ref _value, value);
	}

	/// <summary>Audio thread: where the parameter is now.</summary>
	public float Current => _current;

	/// <summary>The time constant, in seconds, this parameter eases with.</summary>
	public float Seconds => seconds;

	/// <summary>
	/// Game thread, from the source's <c>OnStart</c>: works out the per-sample step for <paramref name="sampleRate"/>.
	/// Until it is called the parameter steps straight to its target.
	/// </summary>
	public void Prepare(int sampleRate) =>
		_keep = seconds <= 0f || sampleRate <= 0 ? 0f : (float)Math.Exp(-1.0 / (seconds * sampleRate));

	/// <summary>Audio thread, once a block: reads the game's <see cref="Value"/> as the target for the block's samples and returns it.</summary>
	public float Begin()
	{
		var target = Volatile.Read(ref _value);
		_error += _target - target;
		if (MathF.Abs(_error) < 1e-20f)
			_error = 0f;
		return _target = target;
	}

	/// <summary>Audio thread, once a sample: steps toward the target <see cref="Begin"/> read and returns where it is.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Next()
	{
		_error *= _keep;
		return _current = _target + _error;
	}

	/// <summary>
	/// Audio thread: steps <paramref name="frames"/> samples at once toward the target <see cref="Begin"/> read, landing
	/// where as many <see cref="Next"/> calls would, and returns <see cref="Current"/>. For a value read once a block.
	/// </summary>
	public float Skip(int frames)
	{
		if (frames <= 0)
			return _current;
		_error = (float)(_error * Math.Pow(_keep, frames));
		return _current = _target + _error;
	}

	/// <summary>Audio thread: jumps straight to <see cref="Value"/>, for a change that should not glide (a new race starting).</summary>
	public float Snap()
	{
		_error = 0f;
		return _current = _target = Volatile.Read(ref _value);
	}
}
