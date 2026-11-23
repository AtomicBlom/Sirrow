namespace BinaryVibrance.Sirrow;

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
		var memory = MemorySeconds;
		_memoryFrames = double.IsFinite(memory) ? (long)Math.Ceiling(Math.Max(memory, 0.0) * sampleRate) : long.MaxValue;
		OnPrepare(sampleRate);
		IsPrepared = true;
	}

	/// <summary>Called once from <see cref="Prepare"/>, on the game thread: size buffers and work out coefficients here.</summary>
	protected virtual void OnPrepare(int sampleRate) { }

	// A block whose every sample is under this is taken as silent: 120 dB under full scale
	private const float Silent = 1e-6f;

	private long _memoryFrames = long.MaxValue;
	private long _quietFrames;
	private volatile bool _resting;

	/// <summary>
	/// How long the effect can still hold sound after its input falls silent without any of it showing at its output, in
	/// seconds: at least its longest delay. Once silence has gone in and come out for this long, the effect has nothing
	/// left to say and <see cref="Run"/> lets it rest. Default 1, which covers a filter, a compressor's envelope or a short
	/// delay; an effect with a longer delay says so, and one that makes sound of its own returns
	/// <see cref="double.PositiveInfinity"/>, never to rest. Read once, in <see cref="Prepare"/>.
	/// </summary>
	protected virtual double MemorySeconds => 1.0;

	/// <summary>Whether <see cref="Run"/> is skipping the effect, with silence going in and nothing left in it. Any thread may read it.</summary>
	public bool IsResting => _resting;

	/// <summary>
	/// What whoever owns the effect calls for each block, in place of <see cref="Process"/>: it processes the block unless
	/// silence has gone in and come out for <see cref="MemorySeconds"/>, after which the block is left alone (it is
	/// silence) until sound comes in again. An effect costs nothing while nothing plays through it. Audio thread.
	/// </summary>
	public void Run(Span<float> left, Span<float> right)
	{
		if (!IsQuiet(left) || !IsQuiet(right))
		{
			_quietFrames = 0;
			if (_resting)
				_resting = false;
			Process(left, right);
			return;
		}
		if (_quietFrames >= _memoryFrames)
		{
			if (!_resting)
				_resting = true;
			return;
		}
		Process(left, right);
		_quietFrames = IsQuiet(left) && IsQuiet(right) ? _quietFrames + left.Length : 0;
	}

	private static bool IsQuiet(ReadOnlySpan<float> samples)
	{
		for (var i = 0; i < samples.Length; i++)
			if (MathF.Abs(samples[i]) >= Silent)
				return false;
		return true;
	}

	/// <summary>
	/// Changes <paramref name="left"/> and <paramref name="right"/> in place: two spans of equal length, at most
	/// <see cref="MaxBlockFrames"/>. Runs on the audio thread. An exception thrown here stops the effect (the sound passes
	/// unchanged from then on) and is thrown again on the game thread, from the engine's next <see cref="AudioEngine.Update"/>.
	/// </summary>
	public abstract void Process(Span<float> left, Span<float> right);
}
