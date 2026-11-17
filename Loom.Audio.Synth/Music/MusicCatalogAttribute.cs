namespace Loom.Audio.Synth.Music;

/// <summary>
/// Names the static class a tool finds an assembly's music in, as <c>[assembly: MusicCatalog(typeof(Catalog))]</c>: a
/// workbench opening the project reads its public static <c>Songs</c>, <c>Patches</c> and <c>Kits</c> (each a list of
/// <c>(string Name, Func&lt;T&gt; Build)</c>), <c>Cues</c> (<c>IReadOnlyList&lt;CueBase&gt;</c>) and, if it has one,
/// <c>MacroName</c> (<c>string? MacroName(int id)</c>), whichever it has. It lives here, not in the workbench, so a game's
/// audio library can carry it without referencing any UI.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class MusicCatalogAttribute(Type catalog) : Attribute
{
	/// <summary>The class holding the music.</summary>
	public Type Catalog { get; } = catalog ?? throw new ArgumentNullException(nameof(catalog));
}
