using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// What the names in a song file stand for when it is loaded at run time (<see cref="SongFile.Load"/>): its patches, kits,
/// cues and macros, and the pieces of it that are code. The generated C# binds the same names at compile time instead.
/// </summary>
public sealed class SongBindings
{
	/// <summary>A patch by the name a track gives it (its file's: "upright-bass").</summary>
	public Func<string, Patch?> Patch { get; init; } = _ => null;

	/// <summary>A kit by the name a track gives it ("general").</summary>
	public Func<string, DrumKit?> Kit { get; init; } = _ => null;

	/// <summary>A cue by its name ("intensity").</summary>
	public Func<string, CueBase?> Cue { get; init; } = _ => null;

	/// <summary>A macro by the name the file gives it ("bright"); a number is always macro that number.</summary>
	public Func<string, Macro?> Macro { get; init; } = _ => null;

	/// <summary>A part that is code (<c>"part": { "code": "Name" }</c>), by its member's name.</summary>
	public Func<string, Part?> Part { get; init; } = _ => null;

	/// <summary>A whole placement that is code (<c>"code": "Name"</c>), made in the section for the track.</summary>
	public Func<string, Section, SongTrack, Placement?> Placement { get; init; } = (_, _, _) => null;

	/// <summary>A hook the file names (<c>"hooks": ["OnBuilt"]</c>), run once the rest is built, with every section.</summary>
	public Action<string, Song, IReadOnlyList<Section>>? Hook { get; init; }
}
