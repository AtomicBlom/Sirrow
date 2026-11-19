using System.Globalization;
using System.Reflection;

using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// Writes a part, or a whole placement, as the C# that makes it, as <see cref="PatchCode"/> writes a patch: for a tool
/// that hands a piece of a song back as code (ejecting it from the song's file), which builds the same piece.
/// </summary>
public static class PartCode
{
	private static readonly (string Name, DrumPiece Piece)[] Pieces = [.. typeof(Kit).GetProperties(BindingFlags.Public | BindingFlags.Static)
		.Where(p => p.PropertyType == typeof(DrumPiece))
		.Select(p => (p.Name, (DrumPiece)p.GetValue(null)!))];

	/// <summary>The C# for <paramref name="part"/>: <c>Line.Eighths("1 3 5 3").OverChords()</c>.</summary>
	/// <exception cref="NotSupportedException">A part of a kind this doesn't write.</exception>
	public static string Write(Part part)
	{
		ArgumentNullException.ThrowIfNull(part);
		switch (part)
		{
			case Line line:
				var text = $"Line.{Step(line.Step)}({Quote(line.Text)})";
				if (line.IsOverChords)
					text += ".OverChords()";
				if (line.OctaveShift != 0)
					text += $".Octave({line.OctaveShift})";
				return text;
			case Beat beat:
				var rows = beat.Rows.Select(r => $"({Piece(r.Piece)}, {Quote(r.Pattern)})").ToArray();
				return rows.Length == 1 ? $"Beat.{Step(beat.Step)}({rows[0]})" : $"Beat.{Step(beat.Step)}(\n\t{string.Join(",\n\t", rows)})";
			case Strum strum:
				var made = $"Strum.{Step(strum.Step)}({Quote(strum.Text)})";
				if (strum.ChordVoicing != Voicing.Open)
					made += $".Voicing(Voicing.{strum.ChordVoicing})";
				if (strum.SpreadMs != 12f)
					made += $".Spread({F(strum.SpreadMs)})";
				return made;
			default:
				throw new NotSupportedException($"A part of a kind this doesn't write: {part.GetType().Name}.");
		}
	}

	/// <summary>
	/// The C# for <paramref name="placement"/>, made by <paramref name="section"/> for <paramref name="track"/> (the names
	/// of the variables it is written with), its cues members of <paramref name="cues"/>:
	/// <c>section.Play(track, Line.Eighths("…")).When(Cues.Intensity.Above(0.5f)).Named("the tune")</c>.
	/// </summary>
	public static string Write(Placement placement, string section, string track, string cues = "Cues")
	{
		ArgumentNullException.ThrowIfNull(placement);
		var text = $"{section}.Play({track}, {Write(placement.Part)})";
		if (placement.Condition is { } condition)
			text += $".When({Write(condition, cues)})";
		if (placement.LevelCue is { } level)
			text += $".Level({Cue(level, cues)})";
		if (placement.IsOnPhrase)
			text += ".OnPhrase()";
		if (placement.FillPart is { } fill)
			text += $".EndWith({Write(fill)})";
		if (placement.Name != placement.Track.Name)
			text += $".Named({Quote(placement.Name)})";
		return text;
	}

	/// <summary>The C# for <paramref name="condition"/>: <c>Cues.Intensity.Above(0.5f) &amp; !Cues.Biome.Is(Biome.Forest)</c>.</summary>
	public static string Write(Condition condition, string cues = "Cues") => Write(condition, cues, 0);

	// Precedence: 0 |, 1 &, 2 ! and the comparisons
	private static string Write(Condition condition, string cues, int context) => condition switch
	{
		AboveCondition above => $"{Cue(above.Cue, cues)}.Above({F(above.Level)})",
		BelowCondition below => $"{Cue(below.Cue, cues)}.Below({F(below.Level)})",
		IsCondition @is => $"{Cue(@is.Cue, cues)}.Is({Option(@is.Cue, @is.Value)})",
		PassCondition pass => $"Pass.{pass.Comparison}({pass.Count})",
		NotCondition not => $"!{Write(not.A, cues, 2)}",
		AndCondition and => Bracket($"{Write(and.A, cues, 1)} & {Write(and.B, cues, 1)}", context > 1),
		OrCondition or => Bracket($"{Write(or.A, cues, 0)} | {Write(or.B, cues, 0)}", context > 0),
		_ => throw new NotSupportedException($"A condition of a kind this doesn't write: {condition.GetType().Name}."),
	};

	private static string Bracket(string text, bool needed) => needed ? $"({text})" : text;

	private static string Cue(CueBase cue, string cues) => $"{cues}.{PatchCode.Pascal(cue.Name)}";

	// A choice cue's value as its enum names it, in full
	private static string Option(CueBase cue, float value)
	{
		var type = cue.GetType();
		var choice = type.IsGenericType ? type.GetGenericArguments()[0] : null;
		for (var i = 0; i < cue.OptionNames.Count; i++)
			if (cue.OptionValue(i) == value && choice is not null)
				return $"global::{choice.FullName!.Replace('+', '.')}.{cue.OptionNames[i]}";
		return F(value);
	}

	private static string Piece(DrumPiece piece)
	{
		foreach (var (name, known) in Pieces)
			if (known == piece)
				return $"Kit.{name}";
		return $"new DrumPiece({Quote(piece.Id)})";
	}

	private static string Step(int step) => step switch
	{
		Ticks.Quarter => "Quarters",
		Ticks.Eighth => "Eighths",
		Ticks.Sixteenth => "Sixteenths",
		Ticks.Triplet => "Triplets",
		_ => throw new NotSupportedException($"A step of {step} ticks, which a part can't be written in."),
	};

	private static string F(float value) => value.ToString(CultureInfo.InvariantCulture) + "f";

	private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
