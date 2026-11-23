using System.Text;

namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// A part's notation as an editor changes it: its steps, each a token (a line's <c>"5"</c>, <c>"-"</c>, <c>"."</c>; a
/// beat's or a strum's one character), between the text's own spaces and bar lines, which are kept as they were. An edit
/// replaces a step's token, so every bar keeps its steps and the text still parses; written back unedited it is the text
/// it was read from, character for character.
/// </summary>
public sealed class NotationText
{
	// The text in pieces: what lies between steps (spaces, bar lines) and the steps themselves, alternately; a step's
	// index into _pieces is _steps[i]
	private readonly List<string> _pieces;
	private readonly List<int> _steps;

	private NotationText(List<string> pieces, List<int> steps, bool characters)
	{
		_pieces = pieces;
		_steps = steps;
		IsCharacters = characters;
	}

	/// <summary>Whether each step is one character (a beat's row, a strum) rather than a token between spaces (a line).</summary>
	public bool IsCharacters { get; }

	/// <summary>How many steps it has.</summary>
	public int Count => _steps.Count;

	/// <summary>The token at <paramref name="step"/>.</summary>
	public string this[int step]
	{
		get => _pieces[_steps[step]];
		set
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(value);
			if (IsCharacters && value.Length != 1)
				throw new ArgumentException($"A step here is one character, not \"{value}\".", nameof(value));
			if (!IsCharacters && value.Any(c => char.IsWhiteSpace(c) || c == '|'))
				throw new ArgumentException($"A step is one token, not \"{value}\".", nameof(value));
			_pieces[_steps[step]] = value;
		}
	}

	/// <summary>The steps' tokens, in order.</summary>
	public IEnumerable<string> Steps => _steps.Select(i => _pieces[i]);

	/// <summary>A line's notes: tokens between spaces and bar lines.</summary>
	public static NotationText Line(string text) => Read(text, characters: false);

	/// <summary>A beat's row or a strum's strokes: a character a step, spaces and bar lines between as the text has them.</summary>
	public static NotationText Characters(string text) => Read(text, characters: true);

	private static NotationText Read(string text, bool characters)
	{
		ArgumentNullException.ThrowIfNull(text);
		var pieces = new List<string>();
		var steps = new List<int>();
		var between = new StringBuilder();
		var i = 0;
		while (i < text.Length)
		{
			var c = text[i];
			if (char.IsWhiteSpace(c) || c == '|')
			{
				between.Append(c);
				i++;
				continue;
			}
			var end = i + 1;
			if (!characters)
				while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '|')
					end++;
			pieces.Add(between.ToString());
			between.Clear();
			steps.Add(pieces.Count);
			pieces.Add(text[i..end]);
			i = end;
		}
		pieces.Add(between.ToString());
		return new NotationText(pieces, steps, characters);
	}

	/// <summary>Where each bar starts, by step: from the text's bar lines, or every <paramref name="perBar"/> steps without them.</summary>
	public IReadOnlyList<int> BarStarts(int perBar)
	{
		var starts = new List<int> { 0 };
		if (_pieces.Any(p => p.Contains('|', StringComparison.Ordinal)))
		{
			for (var s = 1; s < _steps.Count; s++)
				if (_pieces[_steps[s] - 1].Contains('|', StringComparison.Ordinal))
					starts.Add(s);
		}
		else
			for (var s = perBar; s < _steps.Count; s += perBar)
				starts.Add(s);
		return starts;
	}

	/// <summary>
	/// A bar of <paramref name="rest"/> added at the end, spaced as the first bar is, after a bar line; the steps a bar
	/// has from <paramref name="perBar"/>.
	/// </summary>
	public void AddBar(int perBar, string rest)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perBar);
		if (_steps.Count == 0)
			throw new InvalidOperationException("A notation with no steps has no bar to space a new one like.");
		var trailing = _pieces[^1];
		_pieces.RemoveAt(_pieces.Count - 1);
		var first = Math.Min(perBar, _steps.Count);
		for (var s = 0; s < perBar; s++)
		{
			// The space before each step as the first bar has it, a bar line before the new bar's first
			var gap = s == 0 ? (IsCharacters ? " | " : " | ") : s < first ? _pieces[_steps[s] - 1] : (IsCharacters ? "" : " ");
			_pieces.Add(gap);
			_steps.Add(_pieces.Count);
			_pieces.Add(rest);
		}
		_pieces.Add(trailing);
	}

	/// <summary>The last bar taken off, with the bar line before it; refused when it is the only bar.</summary>
	/// <returns>Whether a bar was taken off.</returns>
	public bool RemoveBar(int perBar)
	{
		var starts = BarStarts(perBar);
		if (starts.Count < 2)
			return false;
		var from = starts[^1];
		var keep = _steps[from] - 1;
		var trailing = _pieces[^1];
		_pieces.RemoveRange(keep, _pieces.Count - keep);
		_steps.RemoveRange(from, _steps.Count - from);
		_pieces.Add(trailing);
		return true;
	}

	/// <summary>The text, the spaces and bar lines as they were read.</summary>
	public override string ToString() => string.Concat(_pieces);
}
