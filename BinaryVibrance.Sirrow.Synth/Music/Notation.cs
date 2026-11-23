namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>What the notations share: splitting a text into tokens with their columns, and checking its bars.</summary>
internal static class Notation
{
	// A text's tokens split on white space, each `|` a token of its own, with the column (from 1) each starts at
	public static List<(string Token, int Column)> Tokens(string text)
	{
		var tokens = new List<(string, int)>();
		var start = -1;
		for (var i = 0; i <= text.Length; i++)
		{
			var c = i < text.Length ? text[i] : ' ';
			var boundary = char.IsWhiteSpace(c) || c == '|';
			if (boundary && start >= 0)
			{
				tokens.Add((text[start..i], start + 1));
				start = -1;
			}
			if (c == '|')
				tokens.Add(("|", i + 1));
			else if (!boundary && start < 0)
				start = i;
		}
		return tokens;
	}

	// How many steps of `step` ticks a bar of `meter` holds
	public static int StepsPerBar(string kind, string text, Meter meter, int step)
	{
		if (meter.TicksPerBar % step != 0)
			throw new NotationException($"{kind} \"{text}\": a bar of {meter} isn't a whole number of {Ticks.Name(step)}", text, 0, 0);
		return meter.TicksPerBar / step;
	}

	public static NotationException BarLength(string kind, string text, Meter meter, int step, int bar, int steps, int column) =>
		new($"{kind} \"{text}\": bar {bar} has {steps} steps; a bar of {meter} in {Ticks.Name(step)} has {meter.TicksPerBar / step} (column {column})",
			text, bar, column);
}
