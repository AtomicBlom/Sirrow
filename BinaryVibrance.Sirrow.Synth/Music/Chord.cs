namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// A chord on a degree of the scale, written as a Roman numeral: <c>i</c>, <c>VI5</c>, <c>bVII</c>, <c>V7</c>,
/// <c>ivsus4</c>. Case means nothing: the scale decides whether a triad is major or minor.
/// </summary>
/// <param name="Degree">The degree of its root, 1 to 7.</param>
/// <param name="Kind">What it stacks on its root.</param>
/// <param name="Accidental">Semitones its root is moved off the scale: -1 for a flat.</param>
public readonly record struct Chord(int Degree, ChordKind Kind = ChordKind.Triad, int Accidental = 0)
{
	private static readonly string[] Numerals = ["vii", "iii", "vi", "iv", "ii", "v", "i"];
	private static readonly int[] NumeralDegrees = [7, 3, 6, 4, 2, 5, 1];

	/// <summary>
	/// A chord from its token: <c>b</c> or <c>#</c> as often as wanted, a numeral <c>I</c> to <c>VII</c> in either case,
	/// then nothing, <c>M</c>, <c>m</c>, <c>5</c>, <c>sus2</c>, <c>sus4</c>, <c>7</c> or <c>dim</c>.
	/// </summary>
	/// <exception cref="NotationException">The token isn't a chord.</exception>
	public static Chord Parse(string token)
	{
		ArgumentNullException.ThrowIfNull(token);
		var at = 0;
		var accidental = 0;
		while (at < token.Length && token[at] is 'b' or '#')
			accidental += token[at++] == 'b' ? -1 : 1;
		var degree = 0;
		for (var n = 0; n < Numerals.Length; n++)
			if (token.AsSpan(at).StartsWith(Numerals[n], StringComparison.OrdinalIgnoreCase))
			{
				degree = NumeralDegrees[n];
				at += Numerals[n].Length;
				break;
			}
		if (degree == 0)
			throw new NotationException($"Chord \"{token}\": it has no numeral I to VII (column {at + 1})", token, 0, at + 1);
		var kind = token[at..] switch
		{
			"" => ChordKind.Triad,
			"M" => ChordKind.Major,
			"m" => ChordKind.Minor,
			"5" => ChordKind.Power,
			"sus2" => ChordKind.Sus2,
			"sus4" => ChordKind.Sus4,
			"7" => ChordKind.Seventh,
			"dim" => ChordKind.Diminished,
			var suffix => throw new NotationException(
				$"Chord \"{token}\": \"{suffix}\" isn't one of M, m, 5, sus2, sus4, 7 or dim (column {at + 1})", token, 0, at + 1),
		};
		return new Chord(degree, kind, accidental);
	}

	/// <summary>The pitch class of its root in <paramref name="scale"/>, 0 to 11.</summary>
	public int RootClass(Scale scale) => (((int)scale.Degree(Degree, 0, Accidental).Midi % 12) + 12) % 12;

	/// <summary>The semitones of its notes above its root in <paramref name="scale"/>, rising, the first 0.</summary>
	public int[] Intervals(Scale scale)
	{
		var r = RootClass(scale);
		switch (Kind)
		{
			case ChordKind.Major: return [0, 4, 7];
			case ChordKind.Minor: return [0, 3, 7];
			case ChordKind.Power: return [0, 7];
			case ChordKind.Sus2: return [0, 2, 7];
			case ChordKind.Sus4: return [0, 5, 7];
			case ChordKind.Diminished: return [0, 3, 6];
		}
		var notes = new List<int> { 0 };
		if (scale.Contains(r + 4)) notes.Add(4);
		else if (scale.Contains(r + 3)) notes.Add(3);
		notes.Add(scale.Contains(r + 7) ? 7 : scale.Contains(r + 6) ? 6 : 7);
		if (Kind == ChordKind.Seventh)
			notes.Add(scale.Contains(r + 10) ? 10 : scale.Contains(r + 11) ? 11 : 10);
		return [.. notes];
	}
}
