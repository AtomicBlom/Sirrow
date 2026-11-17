using Microsoft.CodeAnalysis.Diagnostics;

namespace Loom.Audio.Music.Generator;

/// <summary>
/// How a project tells the generator where its music lives: MSBuild properties made visible to it
/// (<c>&lt;CompilerVisibleProperty Include="LoomMusicPatches" /&gt;</c>). The namespace is the project's
/// <c>RootNamespace</c> (else its assembly's name), a song's namespace that and its folder under <c>ProjectDir</c>; the
/// classes names are found in default to <c>Patches</c>, <c>Kits</c>, <c>Cues</c> and <c>Macros</c>.
/// </summary>
internal sealed record MusicOptions(string RootNamespace, string ProjectDir, string Patches, string Kits, string Cues, string Macros)
{
	public static MusicOptions From(AnalyzerConfigOptions options, string assemblyName)
	{
		string Get(string name, string fallback) =>
			options.TryGetValue("build_property." + name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
		return new MusicOptions(Get("RootNamespace", assemblyName), Get("ProjectDir", ""), Get("LoomMusicPatches", "Patches"),
			Get("LoomMusicKits", "Kits"), Get("LoomMusicCues", "Cues"), Get("LoomMusicMacros", "Macros"));
	}

	/// <summary>The namespace for a file at <paramref name="path"/>: the root's, and its folders under the project's.</summary>
	public string NamespaceOf(string path)
	{
		if (ProjectDir.Length == 0)
			return RootNamespace;
		var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
		var root = Path.GetFullPath(ProjectDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			return RootNamespace;
		var parts = folder.Substring(root.Length).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
			.Select(p => Code.Identifier(Code.Pascal(p)));
		return string.Join(".", new[] { RootNamespace }.Concat(parts));
	}
}
