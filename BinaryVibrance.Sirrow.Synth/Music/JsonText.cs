using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// JSON as the music files are written: indented with two spaces, and anything that fits on a line (a part, a track, a
/// row of a beat) kept on one, so a file reads a part to a line and a diff shows the part that changed.
/// </summary>
internal static class JsonText
{
	private const int Width = 120;

	public static string Write(JsonNode node)
	{
		var text = new StringBuilder();
		Write(text, node, 0);
		return text.Append('\n').ToString();
	}

	private static void Write(StringBuilder text, JsonNode? node, int depth)
	{
		var inline = Inline(node);
		// The top level always opens out; below it, whatever fits on its line stays there
		if (depth > 0 && inline.Length + depth * 2 <= Width)
		{
			text.Append(inline);
			return;
		}
		var pad = new string(' ', (depth + 1) * 2);
		switch (node)
		{
			case JsonObject obj:
				text.Append("{\n");
				var i = 0;
				foreach (var (key, value) in obj)
				{
					text.Append(pad).Append(String(key)).Append(": ");
					Write(text, value, depth + 1);
					text.Append(++i < obj.Count ? ",\n" : "\n");
				}
				text.Append(' ', depth * 2).Append('}');
				break;
			case JsonArray array:
				text.Append("[\n");
				for (var j = 0; j < array.Count; j++)
				{
					text.Append(pad);
					Write(text, array[j], depth + 1);
					text.Append(j + 1 < array.Count ? ",\n" : "\n");
				}
				text.Append(' ', depth * 2).Append(']');
				break;
			default:
				text.Append(inline);
				break;
		}
	}

	private static string Inline(JsonNode? node) => node switch
	{
		null => "null",
		JsonObject obj => obj.Count == 0 ? "{}" : "{ " + string.Join(", ", obj.Select(p => $"{String(p.Key)}: {Inline(p.Value)}")) + " }",
		JsonArray array => "[" + string.Join(", ", array.Select(Inline)) + "]",
		JsonValue value when value.TryGetValue<string>(out var s) => String(s),
		JsonValue value when value.TryGetValue<bool>(out var b) => b ? "true" : "false",
		JsonValue value when value.TryGetValue<int>(out var n) => n.ToString(CultureInfo.InvariantCulture),
		JsonValue value when value.TryGetValue<float>(out var f) => f.ToString(CultureInfo.InvariantCulture),
		_ => throw new NotSupportedException($"A value the music files don't write: {node}."),
	};

	private static string String(string value)
	{
		var text = new StringBuilder("\"");
		foreach (var c in value)
		{
			_ = c switch
			{
				'"' => text.Append("\\\""),
				'\\' => text.Append("\\\\"),
				'\n' => text.Append("\\n"),
				'\t' => text.Append("\\t"),
				< ' ' => text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)),
				_ => text.Append(c),
			};
		}
		return text.Append('"').ToString();
	}
}
