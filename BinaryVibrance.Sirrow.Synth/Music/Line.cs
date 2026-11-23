namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// A melody, a bass line or chords, by degree of the scale, a step a token:
/// <code>Line.Eighths("1 3 5 - 4 3 2 . | 1' - 7 5 ^6 5 3~ 2")</code>
/// <c>1</c> to <c>9</c> are degrees (8 the octave above 1); <c>'</c> or <c>,</c> after one moves it an octave up or down
/// each; <c>b</c> or <c>#</c> before one a semitone down or up each; <c>^</c> first accents it; <c>~</c> last slides it
/// into the next; notes joined by <c>/</c> (<c>1/3/5</c>) start together and share their length, a chord; <c>-</c> holds
/// the note (or chord) before a step more; <c>.</c> rests; <c>|</c> ends a bar.
/// </summary>
public sealed class Line : Part
{
	private readonly string _text;
	private readonly int _step;
	private readonly bool _overChords;
	private readonly int _shift;

	private Line(string text, int step, bool overChords, int shift)
	{
		ArgumentNullException.ThrowIfNull(text);
		_text = text;
		_step = step;
		_overChords = overChords;
		_shift = shift;
	}

	/// <summary>A line of quarter notes.</summary>
	public static Line Quarters(string text) => new(text, Ticks.Quarter, false, 0);

	/// <summary>A line of eighth notes.</summary>
	public static Line Eighths(string text) => new(text, Ticks.Eighth, false, 0);

	/// <summary>A line of sixteenth notes.</summary>
	public static Line Sixteenths(string text) => new(text, Ticks.Sixteenth, false, 0);

	/// <summary>A line of eighth-note triplets, three to a beat.</summary>
	public static Line Triplets(string text) => new(text, Ticks.Triplet, false, 0);

	/// <summary>The same line with degree 1 the root of the bar's chord rather than the scale's: a bass line that follows the chords.</summary>
	public Line OverChords() => new(_text, _step, true, _shift);

	/// <summary>The same line <paramref name="shift"/> octaves up (or down, negative).</summary>
	public Line Octave(int shift) => new(_text, _step, _overChords, _shift + shift);

	internal override void Check(SongTrack track)
	{
		if (track.Kit is not null)
			throw new ArgumentException($"Track '{track.Name}' plays a drum kit: give it a Beat, not a Line.", nameof(track));
	}

	internal override ParsedPart Parse(Meter meter)
	{
		var perBar = Notation.StepsPerBar("Line", _text, meter, _step);
		var tokens = Notation.Tokens(_text);
		var notes = new List<RawNote>();
		var hasBars = tokens.Exists(t => t.Token == "|");
		int bar = 1, inBar = 0, steps = 0;
		// The notes the last step started, which a hold lengthens together: one, or a stack
		var sounding = new List<int>();
		var lastColumn = 1;
		foreach (var (token, column) in tokens)
		{
			lastColumn = column;
			if (token == "|")
			{
				if (inBar != perBar)
					throw Notation.BarLength("Line", _text, meter, _step, bar, inBar, column);
				bar++;
				inBar = 0;
				continue;
			}
			if (!hasBars && inBar == perBar)
			{
				bar++;
				inBar = 0;
			}
			var tick = steps * _step;
			steps++;
			inBar++;
			switch (token)
			{
				case "-":
					if (sounding.Count == 0)
						throw new NotationException($"Line \"{_text}\": a hold with no note sounding (column {column})", _text, bar, column);
					foreach (var at in sounding)
						notes[at] = notes[at] with { Length = notes[at].Length + _step };
					continue;
				case ".":
					sounding.Clear();
					continue;
			}
			sounding.Clear();
			foreach (var note in token.Split('/'))
			{
				notes.Add(ParseNote(note.Length == 0 ? token : note, tick, column, bar));
				sounding.Add(notes.Count - 1);
			}
		}
		if (inBar != 0 && inBar != perBar)
			throw Notation.BarLength("Line", _text, meter, _step, bar, inBar, lastColumn);
		if (steps == 0)
			throw new NotationException($"Line \"{_text}\": it has no steps", _text, 0, 0);
		return new Parsed(this, steps * _step, [.. notes]);
	}

	private RawNote ParseNote(string token, int tick, int column, int bar)
	{
		var i = 0;
		var velocity = 0.8f;
		if (token[i] == '^')
		{
			velocity = 1f;
			i++;
		}
		var accidental = 0;
		while (i < token.Length && token[i] is 'b' or '#')
			accidental += token[i++] == 'b' ? -1 : 1;
		if (i >= token.Length || token[i] is < '1' or > '9')
			throw new NotationException($"Line \"{_text}\": \"{token}\" isn't a note (column {column})", _text, bar, column);
		var degree = token[i++] - '0';
		var octave = 0;
		while (i < token.Length && token[i] is '\'' or ',')
			octave += token[i++] == '\'' ? 1 : -1;
		var slide = false;
		if (i < token.Length && token[i] == '~')
		{
			slide = true;
			i++;
		}
		if (i != token.Length)
			throw new NotationException($"Line \"{_text}\": \"{token}\" isn't a note (column {column})", _text, bar, column);
		return new RawNote(tick, _step, degree, octave, accidental, velocity, slide);
	}

	internal readonly record struct RawNote(int Tick, int Length, int Degree, int Octave, int Accidental, float Velocity, bool Slide);

	internal sealed class Parsed(Line line, int lengthTicks, RawNote[] notes) : ParsedPart(lengthTicks)
	{
		public RawNote[] Notes { get; } = notes;

		public override void Emit(PartContext context, int offset, int limit, List<SongEvent> into)
		{
			foreach (var note in Notes)
			{
				var tick = offset + note.Tick;
				if (tick >= limit)
					continue;
				tick = context.Swung(tick);
				var degree = note.Degree;
				var accidental = note.Accidental;
				if (line._overChords)
				{
					var chord = context.ChordAt(tick);
					degree = chord.Degree + degree - 1;
					accidental += chord.Accidental;
				}
				var length = Math.Min(note.Length, limit - tick);
				var pitch = context.Scale.Degree(degree, context.TrackOctave + note.Octave + line._shift, accidental);
				into.Add(new SongEvent
				{
					Tick = tick,
					// A note that doesn't slide is played a little short of its length, so repeated notes are heard as several
					Length = note.Slide ? length : Math.Max(20, (int)Math.Round(0.9 * length)),
					Note = pitch.Midi,
					Piece = -1,
					Velocity = note.Velocity,

				});
			}
		}
	}

	/// <summary>Its notes as written: "1 3 5 3 | 4 - 2 .".</summary>
	public string Text => _text;

	/// <summary>The length of a step, in <see cref="Ticks"/>: <see cref="Ticks.Eighth"/> for <see cref="Eighths"/>.</summary>
	public int Step => _step;

	/// <summary>Whether its degrees are read from each bar's chord (<see cref="OverChords"/>) rather than the scale.</summary>
	public bool IsOverChords => _overChords;

	/// <summary>The octaves it is moved by (<see cref="Octave"/>), on top of its track's.</summary>
	public int OctaveShift => _shift;
}
