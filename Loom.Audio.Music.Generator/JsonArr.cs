using Microsoft.CodeAnalysis.Text;

namespace Loom.Audio.Music.Generator;

internal sealed class JsonArr(IReadOnlyList<Json> values, TextSpan span) : Json(span)
{
	public IReadOnlyList<Json> Values { get; } = values;
}
