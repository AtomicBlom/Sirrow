namespace Loom.Audio.Mixing;

/// <summary>
/// A lock-free ring of interleaved samples between one thread writing and one reading: a stream's decoder and the
/// audio thread. Whole frames go in and come out, since the writer writes only whole frames. Nothing allocates once made.
/// </summary>
internal sealed class SampleRing
{
	private readonly float[] _buffer;
	private readonly int _mask;
	private readonly int _channels;
	private PaddedIndex _written; // samples ever written: the writer's alone
	private PaddedIndex _read;    // samples ever read: the reader's alone

	/// <summary>A ring holding at least <paramref name="frames"/> frames of <paramref name="channels"/> channels.</summary>
	public SampleRing(int frames, int channels)
	{
		var samples = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(frames * channels, 2));
		_buffer = new float[samples];
		_mask = samples - 1;
		_channels = channels;
	}

	/// <summary>Frames waiting to be read.</summary>
	public int Frames => (Volatile.Read(ref _written.Value) - Volatile.Read(ref _read.Value)) / _channels;

	/// <summary>Frames there is room to write.</summary>
	public int FreeFrames => (_buffer.Length - (Volatile.Read(ref _written.Value) - Volatile.Read(ref _read.Value))) / _channels;

	/// <summary>Writer: copies in as many whole frames of <paramref name="samples"/> as fit; returns the frames written.</summary>
	public int Write(ReadOnlySpan<float> samples)
	{
		var written = _written.Value;
		var free = _buffer.Length - (written - Volatile.Read(ref _read.Value));
		var count = Math.Min(free, samples.Length) / _channels * _channels;
		Copy(samples[..count], written);
		Volatile.Write(ref _written.Value, written + count);
		return count / _channels;
	}

	/// <summary>Reader: copies out as many whole frames as wait, up to <paramref name="destination"/>'s length; returns the frames read.</summary>
	public int Read(Span<float> destination)
	{
		var read = _read.Value;
		var waiting = Volatile.Read(ref _written.Value) - read;
		var count = Math.Min(waiting, destination.Length) / _channels * _channels;
		var start = read & _mask;
		var first = Math.Min(count, _buffer.Length - start);
		_buffer.AsSpan(start, first).CopyTo(destination);
		_buffer.AsSpan(0, count - first).CopyTo(destination[first..]);
		Volatile.Write(ref _read.Value, read + count);
		return count / _channels;
	}

	/// <summary>Empties the ring. Only while neither side is using it.</summary>
	public void Reset()
	{
		Volatile.Write(ref _written.Value, 0);
		Volatile.Write(ref _read.Value, 0);
	}

	private void Copy(ReadOnlySpan<float> samples, int at)
	{
		var start = at & _mask;
		var first = Math.Min(samples.Length, _buffer.Length - start);
		samples[..first].CopyTo(_buffer.AsSpan(start));
		samples[first..].CopyTo(_buffer);
	}
}
