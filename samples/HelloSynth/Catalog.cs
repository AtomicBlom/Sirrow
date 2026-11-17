using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Music;

[assembly: MusicCatalog(typeof(HelloSynth.Catalog))]

namespace HelloSynth;

/// <summary>
/// What the workbench finds in this project: its songs, patches and kits, each a factory it calls again after every
/// save, and the cues its music listens to. A song or a patch is a file (Songs/, Patches/) the generator makes a member
/// of: Songs.Demo.Build, Patches.ElectricPiano.
/// </summary>
public static class Catalog
{
	public static IReadOnlyList<(string Name, Func<Song> Build)> Songs { get; } = [("demo", HelloSynth.Songs.Demo.Build)];

	public static IReadOnlyList<(string Name, Func<Patch> Build)> Patches { get; } =
	[
		("electric-piano", () => HelloSynth.Patches.ElectricPiano),
		("picked-bass", () => HelloSynth.Patches.PickedBass),
		("pluck", () => HelloSynth.Patches.Pluck),
		("saw-lead", () => HelloSynth.Patches.SawLead),
	];

	public static IReadOnlyList<(string Name, Func<DrumKit> Build)> Kits { get; } = [("demo-kit", () => HelloSynth.Kits.DemoKit)];

	public static IReadOnlyList<CueBase> Cues { get; } = [HelloSynth.Cues.Intensity, HelloSynth.Cues.Mood];

	/// <summary>How the code names a macro, for the patch designer's C#.</summary>
	public static string? MacroName(int id) => id == Macros.Bright.Id ? "Macros.Bright" : null;
}
