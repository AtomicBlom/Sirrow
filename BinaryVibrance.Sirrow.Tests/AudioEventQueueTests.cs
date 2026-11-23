using System.Runtime.CompilerServices;
using BinaryVibrance.Sirrow;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>The lock-free queue between the game and audio threads: order, capacity, and its two threads at once.</summary>
public class AudioEventQueueTests
{
	[Test]
	public void Events_come_out_in_the_order_they_went_in_across_many_wraps()
	{
		var queue = new AudioEventQueue<int>(4);
		var next = 0;
		for (var round = 0; round < 100; round++)
		{
			for (var i = 0; i < 3; i++)
				queue.TryPost(round * 3 + i).ShouldBeTrue();
			for (var i = 0; i < 3; i++)
			{
				queue.TryTake(out var taken).ShouldBeTrue();
				taken.ShouldBe(next++);
			}
		}
		queue.TryTake(out _).ShouldBeFalse();
	}

	[Test]
	public void The_capacity_rounds_up_to_a_power_of_two_and_a_full_queue_refuses()
	{
		var queue = new AudioEventQueue<int>(5);
		queue.Capacity.ShouldBe(8);
		for (var i = 0; i < 8; i++)
			queue.TryPost(i).ShouldBeTrue();

		queue.TryPost(8).ShouldBeFalse();
		Should.Throw<InvalidOperationException>(() => queue.Post(8));
		queue.Count.ShouldBe(8);
	}

	[Test]
	public void A_taken_slot_lets_go_of_the_object_it_held()
	{
		var queue = new AudioEventQueue<Holder>(2);
		var weak = Post(queue);
		Take(queue);

		GC.Collect();
		GC.WaitForPendingFinalizers();

		weak.IsAlive.ShouldBeFalse();

		// Neither inlined, so no local of this method's (which a debug build keeps alive to its end) holds the object
		[MethodImpl(MethodImplOptions.NoInlining)]
		static WeakReference Post(AudioEventQueue<Holder> queue)
		{
			var payload = new object();
			queue.Post(new Holder(payload));
			return new WeakReference(payload);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		static void Take(AudioEventQueue<Holder> queue) => queue.TryTake(out _).ShouldBeTrue();
	}

	[Test]
	public async Task One_producer_and_one_consumer_on_two_threads_lose_and_reorder_nothing()
	{
		const int Count = 200_000;
		var queue = new AudioEventQueue<int>(64);
		var consumer = Task.Run(() =>
		{
			var expected = 0;
			while (expected < Count)
			{
				if (!queue.TryTake(out var value))
					continue;
				if (value != expected)
					return expected;
				expected++;
			}
			return expected;
		});

		for (var i = 0; i < Count; i++)
			while (!queue.TryPost(i)) { }

		(await consumer).ShouldBe(Count);
	}

	private readonly record struct Holder(object Payload);
}
