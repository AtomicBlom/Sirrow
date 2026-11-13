using System.Runtime.InteropServices;

namespace Loom.Audio;

/// <summary>
/// A fixed-size, lock-free queue from one thread to one other: how a game sends an <see cref="AudioSource"/> discrete
/// events (a note on, a note off, a trigger) in order, and how the engine sends its own commands to the audio thread.
/// Posting and taking allocate nothing and never block, so the audio thread can take from it every block.
/// </summary>
/// <remarks>
/// One thread posts and one thread takes; two posting threads (or two taking) corrupt it. In an audio source the game
/// thread posts and <see cref="AudioSource.Render"/> takes. A taken slot is cleared, so a queue of structs holding object
/// references doesn't keep them alive.
/// </remarks>
/// <typeparam name="T">The event: a small struct, copied in and out.</typeparam>
public sealed class AudioEventQueue<T> where T : struct
{
	private readonly T[] _items;
	private readonly int _mask;
	// On separate cache lines: the producer writes the tail and the consumer the head, every block, from different cores.
	private PaddedIndex _head;
	private PaddedIndex _tail;

	/// <summary>A queue holding at least <paramref name="capacity"/> events (rounded up to a power of two).</summary>
	public AudioEventQueue(int capacity)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, 1 << 30);
		var size = (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)capacity);
		_items = new T[size];
		_mask = size - 1;
	}

	/// <summary>How many events the queue holds when full.</summary>
	public int Capacity => _items.Length;

	/// <summary>How many events are waiting: exact on either thread for its own side, a moment old for the other's.</summary>
	public int Count => Volatile.Read(ref _tail.Value) - Volatile.Read(ref _head.Value);

	/// <summary>Producer: queues <paramref name="item"/>, or returns false when the queue is full.</summary>
	public bool TryPost(in T item)
	{
		var tail = _tail.Value;
		// Wrapping int arithmetic: the difference stays right across overflow while the capacity is under 2^31.
		if (tail - Volatile.Read(ref _head.Value) >= _items.Length)
			return false;
		_items[tail & _mask] = item;
		Volatile.Write(ref _tail.Value, tail + 1); // publishes the item to the consumer
		return true;
	}

	/// <summary>Producer: queues <paramref name="item"/>, throwing when the queue is full rather than dropping it.</summary>
	/// <exception cref="InvalidOperationException">The queue is full: the consumer has fallen behind, or isn't running.</exception>
	public void Post(in T item)
	{
		if (!TryPost(item))
			throw new InvalidOperationException($"The audio event queue is full ({_items.Length} waiting): the audio thread is not taking them. Is the device running?");
	}

	/// <summary>Consumer: takes the oldest event, or returns false when there is none.</summary>
	public bool TryTake(out T item)
	{
		var head = _head.Value;
		if (head == Volatile.Read(ref _tail.Value))
		{
			item = default;
			return false;
		}
		ref var slot = ref _items[head & _mask];
		item = slot;
		slot = default;
		Volatile.Write(ref _head.Value, head + 1); // hands the slot back to the producer
		return true;
	}
}
