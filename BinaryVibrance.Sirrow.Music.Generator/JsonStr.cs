using Microsoft.CodeAnalysis.Text;

namespace BinaryVibrance.Sirrow.Music.Generator;

internal sealed class JsonStr(string value, TextSpan span) : Json(span)
{
	public string Value { get; } = value;
}
