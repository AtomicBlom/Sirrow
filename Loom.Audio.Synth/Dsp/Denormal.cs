using System.Runtime.CompilerServices;

namespace Loom.Audio.Synth.Dsp;

/// <summary>
/// Keeps a filter's memory out of the denormal range: a value decaying toward zero past about 1e-38 makes every
/// operation on it many times slower on most CPUs, which a block of silence through a filter can reach.
/// </summary>
public static class Denormal
{
	/// <summary>Sets <paramref name="x"/> to 0 if its magnitude is under 1e-15, far below anything heard.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Flush(ref float x)
	{
		if (MathF.Abs(x) < 1e-15f)
			x = 0f;
	}
}
