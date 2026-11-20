using System.Globalization;
using System.Text;

namespace Loom.Audio.Music.Generator;

/// <summary>
/// A song file's condition (Loom's ConditionText: <c>"intensity &gt; 0.6 and not biome is Forest"</c>) as C#, a placement's
/// or an exit's <c>Condition</c>: <c>Cues.Intensity.Above(0.6f) &amp; !Cues.Biome.Is(Biome.Forest)</c>. <c>pass</c> is the
/// section's pass in a row (<c>"pass &lt; 2"</c> as <c>Pass.Below(2)</c>).
/// </summary>
internal sealed class ConditionCode(MusicContext context)
{
	private abstract record Node;

	private sealed record Compare(string Cue, string Op, string Value) : Node;

	private sealed record Not(Node A) : Node;

	private sealed record And(Node A, Node B) : Node;

	private sealed record Or(Node A, Node B) : Node;

	/// <summary>The condition as a placement's or an exit's <c>Condition</c>.</summary>
	/// <exception cref="JsonError">It isn't a condition, or names a cue the project doesn't have.</exception>
	public string Placement(Json text) => Write(Parse(text), 0, text);

	// Precedence: 0 or, 1 and, 2 not and comparisons, as ConditionText reads them
	private string Write(Node node, int context, Json where) => node switch
	{
		Compare c => Comparison(c, where),
		Not n => $"!{Write(n.A, 2, where)}",
		And a => Bracket($"{Write(a.A, 1, where)} & {Write(a.B, 1, where)}", context > 1),
		Or o => Bracket($"{Write(o.A, 0, where)} | {Write(o.B, 0, where)}", context > 0),
		_ => throw new InvalidOperationException(),
	};

	private static string Bracket(string text, bool needed) => needed ? $"({text})" : text;

	private string Comparison(Compare c, Json where)
	{
		if (c.Cue == "pass")
		{
			if (!int.TryParse(c.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
				throw new JsonError($"\"{c.Value}\" isn't a whole number of passes", where.Span);
			if (count < 1)
				throw new JsonError($"\"pass {c.Op} {c.Value}\": passes count from 1, the first time through", where.Span);
			var kind = c.Op switch { ">" => "Above", "<" => "Below", _ => "Is" };
			return $"global::Loom.Audio.Synth.Music.Pass.{kind}({count})";
		}
		if (!context.HasCue(c.Cue))
			throw new JsonError($"no cue is named \"{c.Cue}\" in {context.Options.Cues}", where.Span);
		var cue = context.Cue(c.Cue);
		if (c.Op == "is")
		{
			var choice = context.CueEnum(c.Cue) ?? throw new JsonError($"\"{c.Cue}\" isn't a choice, so it can't be \"is {c.Value}\"", where.Span);
			var value = $"{choice}.{Code.Identifier(c.Value)}";
			return $"{cue}.Is({value})";
		}
		if (!float.TryParse(c.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
			throw new JsonError($"\"{c.Value}\" isn't a number", where.Span);
		return $"{cue}.{(c.Op == ">" ? "Above" : "Below")}({Code.F(c.Value)})";
	}

	private static Node Parse(Json text)
	{
		var source = text.Str ?? throw new JsonError("a condition is text (\"intensity > 0.5\")", text.Span);
		var tokens = Tokens(source);
		var at = 0;
		JsonError Error(string why) => new($"\"{source}\" isn't a condition: {why}", text.Span);
		string? Peek() => at < tokens.Count ? tokens[at] : null;
		string Next() => at < tokens.Count ? tokens[at++] : throw Error("it ends too soon");
		Node OrNode()
		{
			var node = AndNode();
			while (Peek() == "or")
			{
				at++;
				node = new Or(node, AndNode());
			}
			return node;
		}
		Node AndNode()
		{
			var node = Unary();
			while (Peek() == "and")
			{
				at++;
				node = new And(node, Unary());
			}
			return node;
		}
		Node Unary()
		{
			var token = Next();
			if (token == "not")
				return new Not(Unary());
			if (token == "(")
			{
				var inner = OrNode();
				if (Next() != ")")
					throw Error("a bracket isn't closed");
				return inner;
			}
			var op = Next();
			if (op is not (">" or "<" or "is"))
				throw Error($"\"{op}\" isn't a comparison (>, < or is)");
			return new Compare(token, op, Next());
		}
		var node = OrNode();
		if (Peek() is { } extra)
			throw Error($"\"{extra}\" is left over");
		return node;
	}

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
}
