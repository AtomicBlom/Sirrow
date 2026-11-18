using System.Globalization;
using System.Text;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// A condition as a song file writes it: <c>"intensity &gt; 0.6"</c>, <c>"intensity &lt; 0.2"</c>, <c>"biome is Forest"</c>,
/// joined by <c>and</c>, <c>or</c> and <c>not</c>, with brackets where the order needs them (<c>and</c> binds tighter
/// than <c>or</c>). Cues are named as they name themselves; a choice cue's value by its option's name.
/// </summary>
public static class ConditionText
{
	/// <summary>The text for <paramref name="condition"/>.</summary>
	public static string Write(Condition condition)
	{
		ArgumentNullException.ThrowIfNull(condition);
		return Write(condition, 0);
	}

	// Precedence: 0 or, 1 and, 2 not and comparisons
	private static string Write(Condition condition, int context) => condition switch
	{
		AboveCondition above => $"{above.Cue.Name} > {Number(above.Level)}",
		BelowCondition below => $"{below.Cue.Name} < {Number(below.Level)}",
		IsCondition @is => $"{@is.Cue.Name} is {Option(@is.Cue, @is.Value)}",
		NotCondition not => $"not {Write(not.A, 2)}",
		AndCondition and => Bracket($"{Write(and.A, 1)} and {Write(and.B, 1)}", context > 1),
		OrCondition or => Bracket($"{Write(or.A, 0)} or {Write(or.B, 0)}", context > 0),
		_ => throw new NotSupportedException($"A condition of a kind a song file can't write: {condition.GetType().Name}."),
	};

	private static string Bracket(string text, bool needed) => needed ? $"({text})" : text;

	private static string Option(CueBase cue, float value)
	{
		var names = cue.OptionNames;
		for (var i = 0; i < names.Count; i++)
			if (cue.OptionValue(i) == value)
				return names[i];
		return Number(value);
	}

	internal static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);

	/// <summary>
	/// The condition <paramref name="text"/> says, its cues found by <paramref name="cue"/>; <c>a and b</c> as
	/// <c>a &amp; b</c>, as code would join them.
	/// </summary>
	/// <exception cref="FormatException">It isn't a condition, or names a cue <paramref name="cue"/> doesn't know.</exception>
	public static Condition Parse(string text, Func<string, CueBase?> cue)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(cue);
		var parser = new Parser(text, cue);
		var condition = parser.Or();
		parser.End();
		return condition;
	}

	private sealed class Parser(string text, Func<string, CueBase?> cues)
	{
		private readonly List<string> _tokens = Tokens(text);
		private int _at;

		private static List<string> Tokens(string text)
		{
			var tokens = new List<string>();
			var word = new StringBuilder();
			void Flush()
			{
				if (word.Length > 0)
					tokens.Add(word.ToString());
				word.Clear();
			}
			foreach (var c in text)
			{
				if (char.IsWhiteSpace(c))
					Flush();
				else if (c is '(' or ')' or '>' or '<')
				{
					Flush();
					tokens.Add(c.ToString());
				}
				else
					word.Append(c);
			}
			Flush();
			return tokens;
		}

		private string? Peek => _at < _tokens.Count ? _tokens[_at] : null;

		private string Next() => _at < _tokens.Count ? _tokens[_at++] : throw Error("it ends too soon");

		private FormatException Error(string why) => new($"\"{text}\" isn't a condition: {why}.");

		public void End()
		{
			if (Peek is { } extra)
				throw Error($"\"{extra}\" is left over");
		}

		public Condition Or()
		{
			var condition = And();
			while (Peek == "or")
			{
				_at++;
				condition = condition | And();
			}
			return condition;
		}

		private Condition And()
		{
			var condition = Unary();
			while (Peek == "and")
			{
				_at++;
				condition = condition & Unary();
			}
			return condition;
		}

		private Condition Unary()
		{
			var token = Next();
			if (token == "not")
				return !Unary();
			if (token == "(")
			{
				var inner = Or();
				if (Next() != ")")
					throw Error("a bracket isn't closed");
				return inner;
			}
			var cue = cues(token) ?? throw Error($"no cue is named \"{token}\"");
			var op = Next();
			var value = Next();
			switch (op, cue)
			{
				case (">", Cue level):
					return level.Above(Level(value));
				case ("<", Cue level):
					return level.Below(Level(value));
				case ("is", _):
					for (var i = 0; i < cue.OptionNames.Count; i++)
						if (string.Equals(cue.OptionNames[i], value, StringComparison.OrdinalIgnoreCase))
							return new IsCondition(cue, cue.OptionValue(i));
					throw Error($"\"{token}\" has no option \"{value}\" ({string.Join(", ", cue.OptionNames)})");
				default:
					throw Error($"\"{token} {op}\" isn't a comparison this cue makes (a level is > or <, a choice is)");
			}
		}

		private float Level(string value) =>
			float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var level) ? level : throw Error($"\"{value}\" isn't a number");
	}
}
