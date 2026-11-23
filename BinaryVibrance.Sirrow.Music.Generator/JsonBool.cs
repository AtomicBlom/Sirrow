using Microsoft.CodeAnalysis.Text;

namespace BinaryVibrance.Sirrow.Music.Generator;

internal sealed class JsonBool(bool value, TextSpan span) : Json(span)
{
	public bool Value { get; } = value;
}
