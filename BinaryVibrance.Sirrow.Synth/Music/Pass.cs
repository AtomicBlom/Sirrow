namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// Conditions on which pass of its section in a row this is, 1 the first time: <c>Pass.Below(2)</c> for a section's
/// exit back to itself once more, <c>Pass.Is(4)</c> for a part on the fourth time round. A song file writes them
/// <c>pass &lt; 2</c>, <c>pass &gt; 1</c>, <c>pass is 4</c>. Whole numbers compare exactly, so <c>&gt;</c> and <c>&lt;</c>
/// are strict here, where a cue's level holds at its threshold. A count below 1 is refused
/// (<see cref="ArgumentOutOfRangeException"/>): passes count from 1, so it could never hold, or always would.
/// </summary>
public static class Pass
{
	/// <summary>True on passes after the <paramref name="count"/>th.</summary>
	public static Condition Above(int count) => new PassCondition(PassComparison.Above, Check(count));

	/// <summary>True on passes before the <paramref name="count"/>th.</summary>
	public static Condition Below(int count) => new PassCondition(PassComparison.Below, Check(count));

	/// <summary>True on the <paramref name="count"/>th pass alone.</summary>
	public static Condition Is(int count) => new PassCondition(PassComparison.Is, Check(count));

	// Passes count from 1, so a count below it is a condition that could never hold, or always would
	private static int Check(int count)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
		return count;
	}
}
