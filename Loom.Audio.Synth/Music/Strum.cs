namespace Loom.Audio.Synth.Music;

/// <summary>
/// Chords strummed across a guitar's strings, a step a character, each chord the section's at that step:
/// <c>D</c> down (low string to high), <c>U</c> up (the top four, high to low), <c>d</c> and <c>u</c> softer, <c>x</c> a
/// muted chuck, <c>.</c> let ring, <c>_</c> stop the strings. Spaces and <c>|</c> mean nothing.
/// <code>Strum.Eighths("D.DU .UDU").Voicing(Voicing.Open)</code>
/// </summary>
public sealed class Strum : Part
{
	// The strings' open notes, low to high: E2 A2 D3 G3 B3 E4
	internal static readonly int[] Strings = [40, 45, 50, 55, 59, 64];

	private readonly string _pattern;
	private readonly int _step;
	private readonly Voicing _voicing;
	private readonly float _spreadMs;

	private Strum(string pattern, int step, Voicing voicing, float spreadMs)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		_pattern = pattern;
		_step = step;
		_voicing = voicing;
		_spreadMs = spreadMs;
	}

	/// <summary>A strum pattern in quarter notes.</summary>
	public static Strum Quarters(string pattern) => new(pattern, Ticks.Quarter, Music.Voicing.Open, 12f);

	/// <summary>A strum pattern in eighth notes.</summary>
	public static Strum Eighths(string pattern) => new(pattern, Ticks.Eighth, Music.Voicing.Open, 12f);

	/// <summary>A strum pattern in sixteenth notes.</summary>
	public static Strum Sixteenths(string pattern) => new(pattern, Ticks.Sixteenth, Music.Voicing.Open, 12f);

	/// <summary>The same pattern in another voicing: <see cref="Music.Voicing.Open"/> unless set.</summary>
	public Strum Voicing(Voicing voicing) => new(_pattern, _step, voicing, _spreadMs);

	/// <summary>The same pattern with <paramref name="milliseconds"/> between one string and the next: 12 unless set.</summary>
	public Strum Spread(float milliseconds) => new(_pattern, _step, _voicing, milliseconds);

	internal override void Check(SongTrack track)
	{
		if (track.Kit is not null)
			throw new ArgumentException($"Track '{track.Name}' plays a drum kit: give it a Beat, not a Strum.", nameof(track));
	}

	internal override ParsedPart Parse(Meter meter)
	{
		var perBar = Notation.StepsPerBar("Strum", _pattern, meter, _step);
		var strokes = new List<(int Tick, char Stroke)>();
		var steps = 0;
		for (var i = 0; i < _pattern.Length; i++)
		{
			var c = _pattern[i];
			if (c == ' ')
				continue;
			// A bar line marks where a bar ends: one that falls inside a bar is a miscount
			if (c == '|')
			{
				if (steps % perBar != 0)
					throw Notation.BarLength("Strum", _pattern, meter, _step, steps / perBar + 1, steps % perBar, i + 1);
				continue;
			}
			if (c is not ('D' or 'U' or 'd' or 'u' or 'x' or '.' or '_'))
				throw new NotationException(
					$"Strum \"{_pattern}\": '{c}' isn't D, U, d, u, x, . or _ (column {i + 1})", _pattern, steps / perBar + 1, i + 1);
			if (c != '.')
				strokes.Add((steps * _step, c));
			steps++;
		}
		if (steps == 0 || steps % perBar != 0)
			throw new NotationException(
				$"Strum \"{_pattern}\": {steps} steps isn't a whole number of bars of {meter} in {Ticks.Name(_step)} ({perBar} each)",
				_pattern, steps / perBar + 1, _pattern.Length);
		return new Parsed(this, steps * _step, [.. strokes]);
	}

	/// <summary>The MIDI notes of a chord with root class <paramref name="root"/> and <paramref name="intervals"/>, low to high.</summary>
	internal static int[] Voice(int root, int[] intervals, Voicing voicing)
	{
		var notes = new List<int>();
		if (voicing == Music.Voicing.Open)
		{
			var tones = new bool[12];
			foreach (var interval in intervals)
				tones[(root + interval) % 12] = true;
			var bass = -1;
			for (var s = 0; s < Strings.Length && bass < 0; s++)
				if (Mod12(root - Strings[s]) <= 4)
					bass = s;
			if (bass < 0)
				bass = 0;
			notes.Add(Strings[bass] + Mod12(root - Strings[bass]));
			for (var s = bass + 1; s < Strings.Length; s++)
				for (var fret = 0; fret <= 4; fret++)
					if (tones[Mod12(Strings[s] + fret)])
					{
						notes.Add(Strings[s] + fret);
						break;
					}
			return [.. notes];
		}
		var low = Strings[0] + Mod12(root - Strings[0]);
		notes.AddRange([low, low + 7, low + 12]);
		if (voicing == Music.Voicing.Triad)
		{
			var third = Array.Find(intervals, i => i is 3 or 4);
			if (third != 0)
				notes.Add(low + 12 + third);
		}
		return [.. notes];
	}

	private static int Mod12(int x) => ((x % 12) + 12) % 12;

	internal sealed class Parsed(Strum strum, int lengthTicks, (int Tick, char Stroke)[] strokes) : ParsedPart(lengthTicks)
	{
		public override void Emit(PartContext context, int offset, int limit, List<SongEvent> into)
		{
			var spread = strum._spreadMs * context.TicksPerMillisecond;
			for (var i = 0; i < strokes.Length; i++)
			{
				var (at, stroke) = strokes[i];
				if (stroke == '_')
					continue;
				var tick = offset + at;
				if (tick >= limit)
					continue;
				// A strum rings until the next stroke, a stop included, or the part's end
				var end = Math.Min(limit, i + 1 < strokes.Length ? context.Swung(offset + strokes[i + 1].Tick) : offset + LengthTicks);
				tick = context.Swung(tick);
				var chord = context.ChordAt(tick);
				var voiced = Voice(chord.RootClass(context.Scale), chord.Intervals(context.Scale), strum._voicing);
				var up = stroke is 'U' or 'u';
				var velocity = stroke switch { 'D' => 0.8f, 'U' => 0.7f, 'x' => 0.5f, _ => 0.5f };
				var count = up ? Math.Min(4, voiced.Length) : voiced.Length;
				for (var k = 0; k < count; k++)
				{
					// Down from the lowest string; up from the highest, over the top four
					var note = up ? voiced[voiced.Length - 1 - k] : voiced[k];
					var start = tick + (int)Math.Round(k * spread);
					var stop = stroke == 'x' ? start + 30 : end;
					if (start >= limit || stop <= start)
						continue;
					into.Add(new SongEvent
					{
						Tick = start,
						Length = Math.Min(stop, limit) - start,
						Note = note,
						Piece = -1,
						Velocity = velocity,
					});
				}
			}
		}
	}
}
