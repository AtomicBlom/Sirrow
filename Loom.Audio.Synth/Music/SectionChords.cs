namespace Loom.Audio.Synth.Music;

/// <summary>
/// A section's chords: bars separated by <c>|</c>, each one chord or two or four sharing the bar evenly. Fewer bars than
/// the section has repeat to fill it.
/// </summary>
internal sealed class SectionChords
{
	private readonly Chord[][] _bars;

	public SectionChords(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		var bars = new List<Chord[]>();
		var column = 1;
		foreach (var bar in text.Split('|'))
		{
			var tokens = bar.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
			if (tokens.Length is not (1 or 2 or 4))
				throw new NotationException(
					$"Chords \"{text}\": bar {bars.Count + 1} has {tokens.Length} chords; a bar has one, two or four (column {column})",
					text, bars.Count + 1, column);
			var chords = new Chord[tokens.Length];
			for (var i = 0; i < tokens.Length; i++)
			{
				try
				{
					chords[i] = Chord.Parse(tokens[i]);
				}
				catch (NotationException ex)
				{
					throw new NotationException($"Chords \"{text}\": bar {bars.Count + 1}: {ex.Message}", text, bars.Count + 1, column);
				}
			}
			bars.Add(chords);
			column += bar.Length + 1;
		}
		_bars = [.. bars];
	}

	public int Bars => _bars.Length;

	public Chord At(int sectionTick, int ticksPerBar)
	{
		var bar = _bars[sectionTick / ticksPerBar % _bars.Length];
		var within = sectionTick % ticksPerBar;
		return bar[Math.Min(bar.Length - 1, within * bar.Length / ticksPerBar)];
	}
}
