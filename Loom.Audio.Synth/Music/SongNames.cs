using System.Text.Json.Nodes;

using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// How a song file names what a built song holds by reference (<see cref="SongFile.ToJson"/>): its patches, kits and
/// macros by the names a game's catalog gives them, and its groups' effects, which a built song can't say.
/// </summary>
public sealed class SongNames
{
	/// <summary>A track's patch's name in the catalog, or null when it has none (the song can't be written).</summary>
	public Func<Patch, string?> Patch { get; init; } = p => p.Name;

	/// <summary>A track's kit's name in the catalog.</summary>
	public Func<DrumKit, string?> Kit { get; init; } = k => k.Name;

	/// <summary>A macro's name ("bright"), or null to write its number.</summary>
	public Func<Macro, string?> Macro { get; init; } = _ => null;

	/// <summary>A group's effects as the file writes them (<c>[{ "fx": "chorus", "mix": 0.4 }]</c>), or null for none.</summary>
	public Func<SongGroup, JsonArray?> GroupEffects { get; init; } = _ => null;
}
