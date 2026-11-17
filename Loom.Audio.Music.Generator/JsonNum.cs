using Microsoft.CodeAnalysis.Text;

namespace Loom.Audio.Music.Generator;

internal sealed class JsonNum(string text, TextSpan span) : Json(span)
{
	public string Text { get; } = text;
}
