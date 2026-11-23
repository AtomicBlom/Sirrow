namespace BinaryVibrance.Sirrow;

/// <summary>
/// An effect that changes nothing and keeps the latest frames of what passes through it (left and right mixed), for a
/// tool on the game thread to draw: a scope, a spectrum. Add it to a bus with <see cref="AudioEngine.AddEffect"/>.
/// </summary>
/// <remarks>
/// The audio thread writes a ring of frames and then publishes how many it has written; <see cref="Read"/> copies the
/// newest without a lock, so a read racing a write can see a block's worth of frames from the next pass. Drawn every
/// frame, that is never seen.
/// </remarks>
public sealed class AudioTap : AudioEffect
{
	private readonly float[] _ring;
	private long _written;

	/// <summary>A tap keeping the latest <paramref name="frames"/> frames.</summary>
	public AudioTap(int frames = 8192)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(frames, MaxBlockFrames);
		_ring = new float[frames];
	}

	/// <summary>How many frames it keeps.</summary>
	public int Capacity => _ring.Length;

	/// <summary>Forever: it always runs, so silence reaches the scope as silence rather than leaving the last sound held.</summary>
	protected override double MemorySeconds => double.PositiveInfinity;

	/// <inheritdoc/>
	public override void Process(Span<float> left, Span<float> right)
	{
		var at = Volatile.Read(ref _written);
		for (var i = 0; i < left.Length; i++)
			_ring[(at + i) % _ring.Length] = 0.5f * (left[i] + right[i]);
		Volatile.Write(ref _written, at + left.Length);
	}

	/// <summary>
	/// Copies the newest frames into <paramref name="into"/>, oldest first, and returns how many: fewer than asked while
	/// it has heard fewer. Any thread.
	/// </summary>
	public int Read(Span<float> into)
	{
		var written = Volatile.Read(ref _written);
		var count = (int)Math.Min(Math.Min(into.Length, _ring.Length), written);
		var start = written - count;
		for (var i = 0; i < count; i++)
			into[i] = _ring[(start + i) % _ring.Length];
		return count;
	}
}
