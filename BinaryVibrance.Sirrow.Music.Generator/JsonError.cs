using Microsoft.CodeAnalysis.Text;

namespace BinaryVibrance.Sirrow.Music.Generator;

/// <summary>What a music file says that the generator can't make into code, and where.</summary>
internal sealed class JsonError(string message, TextSpan span) : Exception(message)
{
	public TextSpan Span { get; } = span;
}
