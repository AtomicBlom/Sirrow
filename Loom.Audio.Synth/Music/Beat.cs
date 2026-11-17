using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// Drums on a grid, a row a piece, a step a character: <c>x</c> a hit (0.75), <c>X</c> an accent (1), <c>o</c> a ghost
/// note (0.35), a digit <c>1</c> to <c>9</c> a hit at that many tenths (for a crescendo a step at a time), <c>.</c> a
/// rest. Spaces and <c>|</c> are for reading and mean nothing.
/// <code>Beat.Eighths((Kit.Kick, "x... x.x. | x... x..."), (Kit.Snare, "..x. ..x. | ..x. ..xo"))</code>
/// </summary>
public sealed class Beat : Part
{
	private readonly (DrumPiece Piece, string Pattern)[] _rows;
	private readonly int _step;

	private Beat((DrumPiece, string)[] rows, int step)
	{
		ArgumentNullException.ThrowIfNull(rows);
		if (rows.Length == 0)
			throw new ArgumentException("A beat has at least one row.", nameof(rows));
		_rows = rows;
		_step = step;
	}

	/// <summary>A beat of eighth notes.</summary>
	public static Beat Eighths(params (DrumPiece Piece, string Pattern)[] rows) => new(rows, Ticks.Eighth);

	/// <summary>A beat of sixteenth notes.</summary>
	public static Beat Sixteenths(params (DrumPiece Piece, string Pattern)[] rows) => new(rows, Ticks.Sixteenth);

	/// <summary>A beat of eighth-note triplets.</summary>
	public static Beat Triplets(params (DrumPiece Piece, string Pattern)[] rows) => new(rows, Ticks.Triplet);

	internal override void Check(SongTrack track)
	{
		if (track.Kit is not { } kit)
			throw new ArgumentException($"Track '{track.Name}' plays a patch: give it a Line or a Strum, not a Beat.", nameof(track));
		foreach (var (piece, _) in _rows)
			if (kit.IndexOf(piece) < 0)
				throw new ArgumentException($"Track '{track.Name}' plays the kit '{kit.Name}', which has no '{piece.Id}'.", nameof(track));
	}

	internal override ParsedPart Parse(Meter meter)
	{
		var perBar = Notation.StepsPerBar("Beat", _rows[0].Pattern, meter, _step);
		var hits = new List<RawHit>();
		var length = -1;
		foreach (var (piece, pattern) in _rows)
		{
			ArgumentNullException.ThrowIfNull(pattern);
			var steps = 0;
			for (var i = 0; i < pattern.Length; i++)
			{
				var c = pattern[i];
				if (c == ' ')
					continue;
				// A bar line marks where a bar ends: one that falls inside a bar is a miscount
				if (c == '|')
				{
					if (steps % perBar != 0)
						throw Notation.BarLength("Beat", pattern, meter, _step, steps / perBar + 1, steps % perBar, i + 1);
					continue;
				}
				var velocity = c switch
				{
					'x' => 0.75f,
					'X' => 1f,
					'o' => 0.35f,
					'.' => 0f,
					>= '1' and <= '9' => (c - '0') / 10f,
					_ => throw new NotationException(
						$"Beat \"{pattern}\": '{c}' isn't x, X, o, a digit 1 to 9 or . (column {i + 1})", pattern, steps / perBar + 1, i + 1),
				};
				if (velocity > 0f)
					hits.Add(new RawHit(steps * _step, piece, velocity));
				steps++;
			}
			if (length >= 0 && steps != length)
				throw new NotationException(
					$"Beat \"{pattern}\": the {piece.Id} row has {steps} steps and the first row {length}: every row is as long", pattern, 0, 0);
			length = steps;
			if (steps == 0 || steps % perBar != 0)
				throw new NotationException(
					$"Beat \"{pattern}\": {steps} steps isn't a whole number of bars of {meter} in {Ticks.Name(_step)} ({perBar} each)",
					pattern, steps / perBar + 1, pattern.Length);
		}
		hits.Sort((a, b) => a.Tick.CompareTo(b.Tick));
		return new Parsed(length * _step, _step, [.. hits]);
	}

	internal readonly record struct RawHit(int Tick, DrumPiece Piece, float Velocity);

	internal sealed class Parsed(int lengthTicks, int step, RawHit[] hits) : ParsedPart(lengthTicks)
	{
		public RawHit[] Hits { get; } = hits;

		public override void Emit(PartContext context, int offset, int limit, List<SongEvent> into)
		{
			var kit = context.Kit!;
			foreach (var hit in Hits)
			{
				var tick = offset + hit.Tick;
				if (tick >= limit)
					continue;
				into.Add(new SongEvent { Tick = tick, Length = step, Note = 60f, Piece = kit.IndexOf(hit.Piece), Velocity = hit.Velocity });
			}
		}
	}
}
