namespace Loom.Audio.Synth.Music;

/// <summary>
/// A scale: a root and the steps above it. Music is written in its degrees (1 the root, 8 the octave above), so a song
/// moves to another key, or another mode, by being played in another scale.
/// </summary>
public sealed class Scale
{
	private readonly int[] _steps;
	private readonly bool[] _classes = new bool[12];

	private Scale(Note root, string name, int[] steps)
	{
		if (steps.Length == 0 || steps[0] != 0)
			throw new ArgumentException("A scale's steps start at 0, the root.", nameof(steps));
		for (var i = 1; i < steps.Length; i++)
			if (steps[i] <= steps[i - 1] || steps[i] >= 12)
				throw new ArgumentException("A scale's steps rise, each under 12.", nameof(steps));
		Root = root;
		Name = name;
		_steps = steps;
		foreach (var step in steps)
			_classes[((int)root + step) % 12] = true;
	}

	/// <summary>Its root, degree 1.</summary>
	public Note Root { get; }

	/// <summary>The semitones of each degree above the root, rising, the first 0.</summary>
	public IReadOnlyList<int> Steps => _steps;

	/// <summary>Its name, such as "minor".</summary>
	public string Name { get; }

	/// <summary>How many notes an octave of it has.</summary>
	public int Length => _steps.Length;

	/// <summary>A scale of one's own: <paramref name="steps"/> semitones above <paramref name="root"/>, rising from 0.</summary>
	public static Scale Of(Note root, string name, params int[] steps)
	{
		ArgumentNullException.ThrowIfNull(steps);
		return new(root, name, [.. steps]);
	}

	/// <summary>The major scale (Ionian): 0 2 4 5 7 9 11.</summary>
	public static Scale Major(Note root) => new(root, "major", [0, 2, 4, 5, 7, 9, 11]);

	/// <summary>The natural minor scale (Aeolian): 0 2 3 5 7 8 10.</summary>
	public static Scale Minor(Note root) => new(root, "minor", [0, 2, 3, 5, 7, 8, 10]);

	/// <summary>Dorian: minor with a raised sixth, 0 2 3 5 7 9 10.</summary>
	public static Scale Dorian(Note root) => new(root, "dorian", [0, 2, 3, 5, 7, 9, 10]);

	/// <summary>Phrygian: minor with a lowered second, 0 1 3 5 7 8 10.</summary>
	public static Scale Phrygian(Note root) => new(root, "phrygian", [0, 1, 3, 5, 7, 8, 10]);

	/// <summary>Lydian: major with a raised fourth, 0 2 4 6 7 9 11.</summary>
	public static Scale Lydian(Note root) => new(root, "lydian", [0, 2, 4, 6, 7, 9, 11]);

	/// <summary>Mixolydian: major with a lowered seventh, 0 2 4 5 7 9 10.</summary>
	public static Scale Mixolydian(Note root) => new(root, "mixolydian", [0, 2, 4, 5, 7, 9, 10]);

	/// <summary>Harmonic minor: minor with a raised seventh, 0 2 3 5 7 8 11.</summary>
	public static Scale HarmonicMinor(Note root) => new(root, "harmonic minor", [0, 2, 3, 5, 7, 8, 11]);

	/// <summary>The major pentatonic: 0 2 4 7 9.</summary>
	public static Scale PentatonicMajor(Note root) => new(root, "pentatonic major", [0, 2, 4, 7, 9]);

	/// <summary>The minor pentatonic: 0 3 5 7 10.</summary>
	public static Scale PentatonicMinor(Note root) => new(root, "pentatonic minor", [0, 3, 5, 7, 10]);

	/// <summary>The blues scale: the minor pentatonic with its flattened fifth, 0 3 5 6 7 10.</summary>
	public static Scale Blues(Note root) => new(root, "blues", [0, 3, 5, 6, 7, 10]);

	/// <summary>
	/// The pitch of <paramref name="degree"/>, counting from 1 (the root in <paramref name="octave"/>): past the scale's
	/// length it climbs into the octaves above, and at 0 or below it falls into those below. <paramref name="accidental"/>
	/// moves it semitones.
	/// </summary>
	public Pitch Degree(int degree, int octave = 4, int accidental = 0)
	{
		var i = degree - 1;
		var o = (int)Math.Floor(i / (double)_steps.Length);
		var k = i - o * _steps.Length;
		return new((octave + 1) * 12 + (int)Root + _steps[k] + 12 * o + accidental);
	}

	/// <summary>Whether pitch class <paramref name="pitchClass"/> (0 to 11, any value wrapped) is in the scale.</summary>
	public bool Contains(int pitchClass) => _classes[((pitchClass % 12) + 12) % 12];

	/// <summary>The same scale from another root.</summary>
	public Scale WithRoot(Note root) => new(root, Name, _steps);

	/// <inheritdoc/>
	public override string ToString() => $"{Root} {Name}";
}
