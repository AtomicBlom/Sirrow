using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Loom.Audio.Music.Generator;

/// <summary>
/// What the generator knows beyond a file: the project's namespace and folder, the classes the music's names are found
/// in (<c>Patches</c>, <c>Kits</c>, <c>Cues</c>, <c>Macros</c> unless the project says otherwise), the patch files beside
/// it, and from the compilation what the text alone can't say: a choice cue's enum, an effect's argument types, a
/// macro's name for its number, the drum pieces Loom names.
/// </summary>
internal sealed class MusicContext(Compilation compilation, MusicOptions options, IReadOnlyCollection<string> patchFiles)
{
	private readonly Dictionary<int, string>? _macroNames = MacroNames(compilation, options.Macros);

	public MusicOptions Options { get; } = options;

	/// <summary>A patch's property, from its file's name: "electric-piano" is "ElectricPiano".</summary>
	public string PatchProperty(string fileName) => Code.Identifier(Code.Pascal(fileName));

	/// <summary>Whether <paramref name="name"/> is a patch the project has: a file beside it, or a member of its patches' class.</summary>
	public bool HasPatch(string name) => patchFiles.Contains(name) || HasMember(Options.Patches, Code.Pascal(name));

	/// <summary>Whether <paramref name="name"/> is a kit the project has.</summary>
	public bool HasKit(string name) => HasMember(Options.Kits, Code.Pascal(name));

	public string Patch(string name) => $"{Options.Patches}.{PatchProperty(name)}";

	public string Kit(string name) => $"{Options.Kits}.{Code.Identifier(Code.Pascal(name))}";

	public string Cue(string name) => $"{Options.Cues}.{Code.Identifier(Code.Pascal(name))}";

	/// <summary>A macro as the file gives it: a name is a member of the macros' class, a number its member or <c>new Macro(n)</c>.</summary>
	public string Macro(Json macro)
	{
		if (macro.Str is { } name)
			return $"{Options.Macros}.{Code.Identifier(Code.Pascal(name))}";
		if (macro.Num is { } number && int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
			return _macroNames is not null && _macroNames.TryGetValue(id, out var member) ? $"{Options.Macros}.{member}" : $"new Macro({id})";
		throw new JsonError("a macro is a name or a number", macro.Span);
	}

	/// <summary>A drum piece by its id: Loom's <c>Kit.HatClosed</c> where it names one, else <c>new DrumPiece("id")</c>.</summary>
	public string Piece(string id)
	{
		var member = Code.Pascal(id);
		var kit = compilation.GetTypeByMetadataName("Loom.Audio.Synth.Chip.Kit");
		return kit is not null && kit.GetMembers(member).Any(m => m.IsStatic) ? $"Kit.{member}" : $"new DrumPiece({Code.Quote(id)})";
	}

	/// <summary>The enum a choice cue chooses among, as C# names it in full, or null for a level (or a cue not found).</summary>
	public string? CueEnum(string name)
	{
		var member = FindMember(Options.Cues, Code.Pascal(name));
		var type = member switch
		{
			IPropertySymbol p => p.Type,
			IFieldSymbol f => f.Type,
			_ => null,
		};
		return type is INamedTypeSymbol { IsGenericType: true } generic && generic.TypeArguments.Length == 1
			? generic.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
			: null;
	}

	/// <summary>Whether the cue's class has a member for <paramref name="name"/>; true when the class itself isn't found.</summary>
	public bool HasCue(string name) => FindType(Options.Cues) is null || FindMember(Options.Cues, Code.Pascal(name)) is not null;

	/// <summary>An effect as the file names it: <c>Fx.Compressor(thresholdDb: -18f, ratio: 3f)</c>.</summary>
	public string Effect(Json effect)
	{
		var fx = effect["fx"]?.Str ?? throw new JsonError("an effect names its factory (\"fx\": \"chorus\")", effect.Span);
		var type = compilation.GetTypeByMetadataName("Loom.Audio.Synth.Effects.Fx");
		var method = type?.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(m => string.Equals(m.Name, fx, StringComparison.OrdinalIgnoreCase));
		if (type is not null && method is null)
			throw new JsonError($"no effect is called \"{fx}\"", effect["fx"]!.Span);
		var arguments = new List<string>();
		foreach (var (key, value, span) in ((JsonObj)effect).Members)
		{
			if (key == "fx")
				continue;
			var parameter = method?.Parameters.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
			if (method is not null && parameter is null)
				throw new JsonError($"{fx} takes no \"{key}\"", span);
			var name = parameter?.Name ?? key;
			arguments.Add($"{name}: {Argument(value, parameter?.Type)}");
		}
		return $"Fx.{method?.Name ?? Code.Pascal(fx)}({string.Join(", ", arguments)})";
	}

	private static string Argument(Json value, ITypeSymbol? type)
	{
		if (value.Num is { } number)
			return type?.SpecialType is SpecialType.System_Int32 ? number : Code.F(number);
		if (value is JsonBool b)
			return b.Value ? "true" : "false";
		if (value.Str is { } text && type is INamedTypeSymbol { TypeKind: TypeKind.Enum } choice)
		{
			var member = choice.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => string.Equals(f.Name, text, StringComparison.OrdinalIgnoreCase))
				?? throw new JsonError($"\"{text}\" isn't a {choice.Name} ({string.Join(", ", choice.GetMembers().OfType<IFieldSymbol>().Select(f => f.Name))})", value.Span);
			return $"{choice.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{member.Name}";
		}
		throw new JsonError($"{value} isn't an argument this can write", value.Span);
	}

	private INamedTypeSymbol? FindType(string name) =>
		compilation.GetSymbolsWithName(name, SymbolFilter.Type).OfType<INamedTypeSymbol>()
			.OrderBy(t => t.ContainingNamespace.ToDisplayString() == Options.RootNamespace ? 0 : 1)
			.FirstOrDefault();

	private ISymbol? FindMember(string type, string member) => FindType(type)?.GetMembers(member).FirstOrDefault(m => m.IsStatic);

	private bool HasMember(string type, string member) => FindType(type) is null || FindMember(type, member) is not null;

	// The macros' class's members by the number each is made with: "public static Macro Bright { get; } = new(0);"
	private static Dictionary<int, string>? MacroNames(Compilation compilation, string macros)
	{
		var type = compilation.GetSymbolsWithName(macros, SymbolFilter.Type).OfType<INamedTypeSymbol>().FirstOrDefault();
		if (type is null)
			return null;
		var names = new Dictionary<int, string>();
		foreach (var reference in type.DeclaringSyntaxReferences)
			foreach (var node in reference.GetSyntax().DescendantNodes())
			{
				var (name, value) = node switch
				{
					PropertyDeclarationSyntax p => (p.Identifier.ValueText, p.Initializer?.Value),
					VariableDeclaratorSyntax v => (v.Identifier.ValueText, v.Initializer?.Value),
					_ => ("", null),
				};
				var arguments = value switch
				{
					ImplicitObjectCreationExpressionSyntax made => made.ArgumentList.Arguments,
					ObjectCreationExpressionSyntax made => made.ArgumentList?.Arguments ?? default,
					_ => default,
				};
				if (arguments.Count == 1 && arguments[0].Expression is LiteralExpressionSyntax literal && literal.Token.Value is int id && !names.ContainsKey(id))
					names[id] = name;
			}
		return names;
	}
}
