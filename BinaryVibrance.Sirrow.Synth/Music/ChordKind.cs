namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>What a <see cref="Chord"/> stacks on its root.</summary>
public enum ChordKind
{
	/// <summary>The scale's own triad on that degree: major, minor or diminished as the scale makes it.</summary>
	Triad,
	/// <summary>A major triad whatever the scale: 0 4 7.</summary>
	Major,
	/// <summary>A minor triad whatever the scale: 0 3 7.</summary>
	Minor,
	/// <summary>A power chord, root and fifth: 0 7.</summary>
	Power,
	/// <summary>The second instead of the third: 0 2 7.</summary>
	Sus2,
	/// <summary>The fourth instead of the third: 0 5 7.</summary>
	Sus4,
	/// <summary>The scale's triad and its seventh.</summary>
	Seventh,
	/// <summary>A diminished triad: 0 3 6.</summary>
	Diminished,
}
