namespace Loom.Audio.Synth.Music;

/// <summary>
/// A mistake in a notation (a line, a beat, a strum, a section's chords), thrown when the song is built. Its message
/// quotes the text and says where: <c>Line "1 2 3": bar 1 has 3 steps; a bar of 4/4 in quarters has 4 (column 5)</c>.
/// </summary>
public sealed class NotationException(string message, string text, int bar, int column) : Exception(message)
{
	/// <summary>The notation's text.</summary>
	public string Text { get; } = text;

	/// <summary>The bar the mistake is in, from 1; 0 where it isn't a bar's.</summary>
	public int Bar { get; } = bar;

	/// <summary>The column of the text it is at, from 1; 0 where it isn't one place.</summary>
	public int Column { get; } = column;
}
