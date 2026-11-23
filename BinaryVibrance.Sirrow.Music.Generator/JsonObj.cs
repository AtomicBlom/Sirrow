using Microsoft.CodeAnalysis.Text;

namespace BinaryVibrance.Sirrow.Music.Generator;

internal sealed class JsonObj(IReadOnlyList<(string Key, Json Value, TextSpan KeySpan)> members, TextSpan span) : Json(span)
{
	public IReadOnlyList<(string Key, Json Value, TextSpan KeySpan)> Members { get; } = members;

	public Json? Get(string key)
	{
		foreach (var (k, v, _) in Members)
			if (k == key)
				return v;
		return null;
	}
}
