namespace BinaryVibrance.Sirrow.Tests;

/// <summary>Counting the bytes a stretch of steady-state work allocates on the test's thread.</summary>
internal static class Allocations
{
	/// <summary>
	/// Assert that <paramref name="window"/> allocates nothing on this thread in the steady state. The window is run up to
	/// <paramref name="attempts"/> times and passes on the first that allocates nothing: an allocation the work makes
	/// every block or frame recurs in every window and fails. Under the full parallel suite a single window has
	/// occasionally counted a one-off few KB that the same window, run alone or again, does not (the runtime's own, as
	/// code tiers up); a failure lists every window's count and the gen-0 collections that ran during it.
	/// </summary>
	public static void ShouldAllocateNothing(Action window, string? because = null, int attempts = 5)
	{
		if (Allocated(window, attempts) is { } log)
			throw new ShouldAssertException((because is null ? "" : because + " ") + log);
	}

	/// <summary>
	/// <see cref="ShouldAllocateNothing"/> for a test that gathers its failures (one per backend, say) before asserting:
	/// null when a window allocated nothing, otherwise every window's count.
	/// </summary>
	public static string? Allocated(Action window, int attempts = 5)
	{
		var log = new (long Bytes, int Collections)[attempts];
		for (var i = 0; i < attempts; i++)
		{
			var collections = GC.CollectionCount(0);
			var before = GC.GetAllocatedBytesForCurrentThread();
			window();
			log[i] = (GC.GetAllocatedBytesForCurrentThread() - before, GC.CollectionCount(0) - collections);
			if (log[i].Bytes == 0)
				return null;
		}
		return "Every window allocated: " + string.Join(", ", log.Select(static a => $"{a.Bytes} B ({a.Collections} gen-0 collection(s))"));
	}
}
