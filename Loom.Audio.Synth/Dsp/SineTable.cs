using System.Runtime.CompilerServices;

namespace Loom.Audio.Synth.Dsp;

/// <summary>
/// A sine read from a table of one cycle, interpolated between its 4096 steps: within 1e-5 of the true sine, and much
/// cheaper per sample than <see cref="MathF.Sin(float)"/>.
/// </summary>
public static class SineTable
{
	private const int Size = 4096;
	private static readonly float[] Table = Make();

	private static float[] Make()
	{
		var table = new float[Size + 1];
		for (var i = 0; i <= Size; i++)
			table[i] = (float)Math.Sin(2 * Math.PI * i / Size);
		return table;
	}

	/// <summary>The sine at <paramref name="phase"/>, in cycles: any value, negative or past 1, wraps.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float At(float phase)
	{
		phase -= MathF.Floor(phase);
		var x = phase * Size;
		var i = (int)x;
		if (i >= Size)
			i = Size - 1;
		var a = Table[i];
		return a + (Table[i + 1] - a) * (x - i);
	}
}
