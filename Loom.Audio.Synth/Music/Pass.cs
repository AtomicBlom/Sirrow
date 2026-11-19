namespace Loom.Audio.Synth.Music;

/// <summary>
/// Conditions on which pass of its section in a row this is, 1 the first time: <c>Pass.Below(2)</c> for a section's
/// exit back to itself once more, <c>Pass.Is(4)</c> for a part on the fourth time round. A song file writes them
/// <c>pass &lt; 2</c>, <c>pass &gt; 1</c>, <c>pass is 4</c>.
/// </summary>
public static class Pass
{
	/// <summary>True on passes after the <paramref name="count"/>th.</summary>
	public static Condition Above(int count) => new PassCondition(PassComparison.Above, Check(count));

	/// <summary>True on passes before the <paramref name="count"/>th.</summary>
	public static Condition Below(int count) => new PassCondition(PassComparison.Below, Check(count));

	/// <summary>True on the <paramref name="count"/>th pass alone.</summary>
	public static Condition Is(int count) => new PassCondition(PassComparison.Is, Check(count));

	private static int Check(int count)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(count);
		return count;
	}
}
