using System.Globalization;
using System.Reflection;
using System.Text.Json;

using BinaryVibrance.Sirrow.Synth.Chip;
using BinaryVibrance.Sirrow.Synth.Effects;

namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// An effect as a song file names it: one of <see cref="Fx"/>'s factories and its arguments by their parameter names,
/// <c>{ "fx": "compressor", "thresholdDb": -18, "ratio": 3 }</c>, the rest left at the factory's defaults. Sirrow's music
/// generator writes the same call (<c>Fx.Compressor(thresholdDb: -18f, ratio: 3f)</c>).
/// </summary>
internal static class EffectText
{
	private static readonly MethodInfo[] Factories = typeof(Fx).GetMethods(BindingFlags.Public | BindingFlags.Static);

	/// <summary>The effect an element names.</summary>
	/// <exception cref="FormatException">It names no factory, or an argument the factory doesn't take.</exception>
	public static Func<AudioEffect> Make(JsonElement element, string song)
	{
		var name = PatchFile.String(element, "fx") ?? throw new FormatException($"Song '{song}': an effect names its factory (\"fx\": \"chorus\").");
		var factory = Array.Find(Factories, f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) && f.ReturnType == typeof(Func<AudioEffect>))
			?? throw new FormatException($"Song '{song}': no effect is called \"{name}\" ({string.Join(", ", Factories.Select(f => Camel(f.Name)).Distinct())}).");
		var parameters = factory.GetParameters();
		var arguments = parameters.Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
		var given = new bool[parameters.Length];
		foreach (var property in element.EnumerateObject())
		{
			if (property.Name == "fx")
				continue;
			var at = Array.FindIndex(parameters, p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
			if (at < 0)
				throw new FormatException($"Song '{song}': {Camel(factory.Name)} takes no \"{property.Name}\" ({string.Join(", ", parameters.Select(p => p.Name))}).");
			arguments[at] = Value(property.Value, parameters[at].ParameterType, song, property.Name);
			given[at] = true;
		}
		for (var i = 0; i < parameters.Length; i++)
			if (!given[i] && !parameters[i].HasDefaultValue)
				throw new FormatException($"Song '{song}': {Camel(factory.Name)} needs \"{parameters[i].Name}\".");
		return (Func<AudioEffect>)factory.Invoke(null, arguments)!;
	}

	private static object Value(JsonElement value, Type type, string song, string name)
	{
		try
		{
			if (type == typeof(float))
				return value.GetSingle();
			if (type == typeof(int))
				return value.GetInt32();
			if (type == typeof(bool))
				return value.GetBoolean();
			if (type.IsEnum && value.GetString() is { } text)
				return Enum.Parse(type, text, ignoreCase: true);
		}
		catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
		{
		}
		throw new FormatException($"Song '{song}': \"{name}\": {value} isn't a {type.Name}.");
	}

	internal static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

	internal static string Number(float value) => value.ToString(CultureInfo.InvariantCulture);
}
