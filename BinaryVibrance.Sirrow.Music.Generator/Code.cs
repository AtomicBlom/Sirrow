using System.Globalization;
using System.Text;

namespace BinaryVibrance.Sirrow.Music.Generator;

/// <summary>Writing C#: names made identifiers, numbers made float literals, strings quoted, comments wrapped.</summary>
internal static class Code
{
	private static readonly HashSet<string> Keywords =
	[
		"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
		"decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
		"fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
		"namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
		"readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
		"switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
		"virtual", "void", "volatile", "while",
	];

	/// <summary>"electric-piano" as "ElectricPiano", as Sirrow's PatchCode.Pascal makes it.</summary>
	public static string Pascal(string name)
	{
		var text = new StringBuilder();
		foreach (var word in name.Split(['-', '_', ' ', '.'], StringSplitOptions.RemoveEmptyEntries))
			text.Append(char.ToUpperInvariant(word[0])).Append(word.Substring(1));
		return text.ToString();
	}

	/// <summary>"busy hats" as "busyHats", a variable's name, never a keyword.</summary>
	public static string Camel(string name)
	{
		var pascal = Identifier(Pascal(name));
		if (pascal.Length == 0)
			return "_";
		var camel = char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
		return Keywords.Contains(camel) ? "@" + camel : camel;
	}

	/// <summary>Only the letters, digits and underscores of <paramref name="name"/>, not starting with a digit.</summary>
	public static string Identifier(string name)
	{
		var text = new string([.. name.Where(c => char.IsLetterOrDigit(c) || c == '_')]);
		return text.Length > 0 && char.IsDigit(text[0]) ? "_" + text : text;
	}

	/// <summary>A JSON number's text as a float literal: "0.35" is "0.35f".</summary>
	public static string F(string number) => number + "f";

	/// <summary>A float as C# writes it, the shortest text that reads back as the same float.</summary>
	public static string F(float value)
	{
		var text = value.ToString("R", CultureInfo.InvariantCulture);
		return text + "f";
	}

	/// <summary>A JSON number's value, for comparing with a default.</summary>
	public static float Value(string number) => float.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);

	/// <summary>A C# string literal.</summary>
	public static string Quote(string text)
	{
		var quoted = new StringBuilder("\"");
		foreach (var c in text)
		{
			switch (c)
			{
				case '"': quoted.Append("\\\""); break;
				case '\\': quoted.Append("\\\\"); break;
				case '\n': quoted.Append("\\n"); break;
				case '\r': quoted.Append("\\r"); break;
				case '\t': quoted.Append("\\t"); break;
				default: quoted.Append(c); break;
			}
		}
		return quoted.Append('"').ToString();
	}

	/// <summary><paramref name="text"/> as // lines no wider than 120 with <paramref name="indent"/> before them.</summary>
	public static IEnumerable<string> Comment(string text, string indent)
	{
		var line = new StringBuilder();
		foreach (var word in text.Split([' '], StringSplitOptions.RemoveEmptyEntries))
		{
			if (line.Length > 0 && indent.Length + 3 + line.Length + 1 + word.Length > 120)
			{
				yield return indent + "// " + line;
				line.Clear();
			}
			if (line.Length > 0)
				line.Append(' ');
			line.Append(word);
		}
		if (line.Length > 0)
			yield return indent + "// " + line;
	}
}
