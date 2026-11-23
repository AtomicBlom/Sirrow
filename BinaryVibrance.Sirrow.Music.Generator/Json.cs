using System.Globalization;
using System.Text;

using Microsoft.CodeAnalysis.Text;

namespace BinaryVibrance.Sirrow.Music.Generator;

/// <summary>
/// A JSON value as the music files hold it, with where it starts in its file, so a diagnostic can point at it. Numbers
/// keep their text, which the generator writes into C# as it is (shortest round-trip, as the files are written).
/// </summary>
internal abstract class Json(TextSpan span)
{
	public TextSpan Span { get; } = span;

	public Json? this[string key] => this is JsonObj obj ? obj.Get(key) : null;

	public string? Str => this is JsonStr s ? s.Value : null;

	public string? Num => this is JsonNum n ? n.Text : null;

	public bool True => this is JsonBool { Value: true };

	public IReadOnlyList<Json> Items => this is JsonArr a ? a.Values : [];

	/// <summary>Parses <paramref name="text"/>, comments and trailing commas allowed.</summary>
	/// <exception cref="JsonError">It isn't JSON.</exception>
	public static Json Parse(string text) => new Reader(text).Document();

	private sealed class Reader(string text)
	{
		private int _at;

		public Json Document()
		{
			var value = Value();
			Skip();
			if (_at < text.Length)
				throw Error("something follows the value");
			return value;
		}

		private JsonError Error(string why) => new(why, new TextSpan(Math.Min(_at, text.Length), 0));

		private void Skip()
		{
			while (_at < text.Length)
			{
				if (char.IsWhiteSpace(text[_at]))
					_at++;
				else if (text[_at] == '/' && _at + 1 < text.Length && text[_at + 1] == '/')
					while (_at < text.Length && text[_at] != '\n')
						_at++;
				else if (text[_at] == '/' && _at + 1 < text.Length && text[_at + 1] == '*')
				{
					var end = text.IndexOf("*/", _at + 2, StringComparison.Ordinal);
					_at = end < 0 ? text.Length : end + 2;
				}
				else
					return;
			}
		}

		private Json Value()
		{
			Skip();
			if (_at >= text.Length)
				throw Error("it ends where a value should be");
			var start = _at;
			switch (text[_at])
			{
				case '{':
					return Object();
				case '[':
					return Array();
				case '"':
					var value = String();
					return new JsonStr(value, new TextSpan(start, _at - start));
				case 't' when Word("true"):
					return new JsonBool(true, new TextSpan(start, 4));
				case 'f' when Word("false"):
					return new JsonBool(false, new TextSpan(start, 5));
				case 'n' when Word("null"):
					return new JsonNull(new TextSpan(start, 4));
				default:
					while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] is '-' or '+' or '.' or 'e' or 'E'))
						_at++;
					var number = text.Substring(start, _at - start);
					if (number.Length == 0 || !double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
					{
						_at = start;
						throw Error("this isn't a value");
					}
					return new JsonNum(number, new TextSpan(start, number.Length));
			}
		}

		private bool Word(string word)
		{
			if (string.CompareOrdinal(text, _at, word, 0, word.Length) != 0)
				return false;
			_at += word.Length;
			return true;
		}

		private JsonObj Object()
		{
			var start = _at++;
			var members = new List<(string, Json, TextSpan)>();
			while (true)
			{
				Skip();
				if (_at < text.Length && text[_at] == '}')
					break;
				if (_at >= text.Length || text[_at] != '"')
					throw Error("a name in quotes should be here");
				var keyStart = _at;
				var key = String();
				var keySpan = new TextSpan(keyStart, _at - keyStart);
				Skip();
				if (_at >= text.Length || text[_at] != ':')
					throw Error("a ':' should follow the name");
				_at++;
				members.Add((key, Value(), keySpan));
				Skip();
				if (_at < text.Length && text[_at] == ',')
				{
					_at++;
					continue;
				}
				Skip();
				if (_at >= text.Length || text[_at] != '}')
					throw Error("a ',' or '}' should be here");
				break;
			}
			_at++;
			return new JsonObj(members, new TextSpan(start, _at - start));
		}

		private JsonArr Array()
		{
			var start = _at++;
			var values = new List<Json>();
			while (true)
			{
				Skip();
				if (_at < text.Length && text[_at] == ']')
					break;
				values.Add(Value());
				Skip();
				if (_at < text.Length && text[_at] == ',')
				{
					_at++;
					continue;
				}
				Skip();
				if (_at >= text.Length || text[_at] != ']')
					throw Error("a ',' or ']' should be here");
				break;
			}
			_at++;
			return new JsonArr(values, new TextSpan(start, _at - start));
		}

		private string String()
		{
			_at++;
			var value = new StringBuilder();
			while (_at < text.Length && text[_at] != '"')
			{
				if (text[_at] == '\\' && _at + 1 < text.Length)
				{
					_at++;
					switch (text[_at])
					{
						case 'n': value.Append('\n'); break;
						case 't': value.Append('\t'); break;
						case 'r': value.Append('\r'); break;
						case 'b': value.Append('\b'); break;
						case 'f': value.Append('\f'); break;
						case 'u' when _at + 4 < text.Length:
							value.Append((char)int.Parse(text.Substring(_at + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
							_at += 4;
							break;
						default: value.Append(text[_at]); break;
					}
					_at++;
				}
				else
					value.Append(text[_at++]);
			}
			if (_at >= text.Length)
				throw Error("a string isn't closed");
			_at++;
			return value.ToString();
		}
	}
}
