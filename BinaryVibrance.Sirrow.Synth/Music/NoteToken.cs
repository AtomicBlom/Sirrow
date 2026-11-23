using System.Text;

namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// One note of a <see cref="Line"/> as its notation writes it: <c>^b3'~</c> is an accented flat third an octave up,
/// sliding into the next. In order: <c>^</c> accent, <c>b</c> or <c>#</c> each a semitone, the degree 1 to 9, <c>'</c>
/// or <c>,</c> each an octave, <c>~</c> slide.
/// </summary>
/// <param name="Degree">The scale's degree, 1 to 9 (8 and 9 the octave and the ninth).</param>
/// <param name="Octave">Octaves up (above 0) or down.</param>
/// <param name="Accidental">Semitones sharp (above 0) or flat.</param>
/// <param name="Accent">Whether it is played hard.</param>
/// <param name="Slide">Whether it slides into the next note.</param>
public readonly record struct NoteToken(int Degree, int Octave = 0, int Accidental = 0, bool Accent = false, bool Slide = false)
{
	/// <summary>The row an editor shows it on, by degree and octave alone: degree 1 is 0, each octave seven rows.</summary>
	public int Row => Degree - 1 + 7 * Octave;

	/// <summary>The note on <paramref name="row"/>, its degree 1 to 7 and the octave that row is in.</summary>
	public static NoteToken OnRow(int row)
	{
		var octave = (int)Math.Floor(row / 7.0);
		return new NoteToken(row - 7 * octave + 1, octave);
	}

	/// <summary>The note a token is, or null for a hold, a rest or anything else.</summary>
	public static NoteToken? Parse(string token)
	{
		ArgumentNullException.ThrowIfNull(token);
		var i = 0;
		var accent = i < token.Length && token[i] == '^';
		if (accent)
			i++;
		var accidental = 0;
		while (i < token.Length && token[i] is 'b' or '#')
			accidental += token[i++] == 'b' ? -1 : 1;
		if (i >= token.Length || token[i] is < '1' or > '9')
			return null;
		var degree = token[i++] - '0';
		var octave = 0;
		while (i < token.Length && token[i] is '\'' or ',')
			octave += token[i++] == '\'' ? 1 : -1;
		var slide = i < token.Length && token[i] == '~';
		if (slide)
			i++;
		return i == token.Length ? new NoteToken(degree, octave, accidental, accent, slide) : null;
	}

	/// <summary>
	/// The notes a line's step starts: one, or several stacked with <c>/</c> (<c>1/3/5</c>) to sound together and share a
	/// length. Null for a hold, a rest, or anything one of whose notes isn't one.
	/// </summary>
	public static IReadOnlyList<NoteToken>? ParseStack(string token)
	{
		ArgumentNullException.ThrowIfNull(token);
		var parts = token.Split('/');
		var notes = new NoteToken[parts.Length];
		for (var i = 0; i < parts.Length; i++)
		{
			if (Parse(parts[i]) is not { } note)
				return null;
			notes[i] = note;
		}
		return notes;
	}

	/// <summary>Notes written as one step of a line: lowest first, joined by <c>/</c>, one to a row (the last given for a row wins).</summary>
	public static string Stack(IEnumerable<NoteToken> notes)
	{
		ArgumentNullException.ThrowIfNull(notes);
		return string.Join('/', notes.GroupBy(n => n.Row).Select(g => g.Last()).OrderBy(n => n.Row).Select(n => n.ToString()));
	}

	/// <summary>The token, as a line's notation writes it.</summary>
	public override string ToString()
	{
		var text = new StringBuilder();
		if (Accent)
			text.Append('^');
		text.Append(Accidental < 0 ? 'b' : '#', Math.Abs(Accidental));
		text.Append(Degree);
		text.Append(Octave < 0 ? ',' : '\'', Math.Abs(Octave));
		if (Slide)
			text.Append('~');
		return text.ToString();
	}
}
