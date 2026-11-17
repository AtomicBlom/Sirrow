using System.Collections.Immutable;
using System.Reflection;

using Loom.Audio.Music.Generator;
using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Music;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Loom.Tests;

/// <summary>
/// Loom's music generator over a small project of its own: a patch file and a song file made into C# that builds what
/// the run-time loaders build from the same files, and a file's mistake reported at its line.
/// </summary>
public class MusicGeneratorTests
{
	private const string Source = """
		using Loom.Audio.Synth.Chip;
		using Loom.Audio.Synth.Music;

		namespace Tunes
		{
			public enum Mood { Calm, Dark }

			public static class Cues
			{
				public static Cue Intensity { get; } = new("intensity", 0.3f);
				public static Cue<Mood> Mood { get; } = new("mood");
			}

			public static class Macros
			{
				public static Macro Bright { get; } = new(0);
			}

			public static class Kits
			{
				public static DrumKit General => DrumKit.FromPatches("general", (Kit.Kick, Patches.Pluck, 0), (Kit.Snare, Patches.Pluck, 0));
			}

			static partial class Patches
			{
			}
		}

		// The song's code, in the namespace of its file's folder (Songs)
		namespace Tunes.Songs
		{
			static partial class Tune
			{
				private static partial Part Riff() => Line.Eighths("1 3 5 3 1 3 5 8");

				static partial void OnBuilt(Song song, Section verse, Section chorus) => verse.Play(song.Tracks[1], Line.Quarters("8 . 5 ."));
			}
		}
		""";

	private static PatchSpec PluckSpec()
	{
		var spec = PatchSpec.From(Patch.Build("pluck", p =>
		{
			var pick = p.Sine(3f, Env.Decay(0.05f), 0.7f, velocity: 0.5f);
			p.Out(p.Sine(1f, Env.Adsr(0.002f, 0.4f, 0.2f, 0.1f)).ModulatedBy(pick, 0.4f).Detune(7f), p.Noise(Env.Decay(0.01f), 0.2f, pink: true));
			p.Filter(Filter.LowPass(1800f, q: 1.1f, envelopeOctaves: 2f, envelope: Env.Decay(0.3f)));
			p.Macro(new Macro(0), MacroTarget.FilterCutoff, 0.5f, 2f);
			p.Send(ChipSend.Space, 0.15f);
			p.Mono(0.03f);
		}));
		spec.About = "A plucked saw, for the test";
		return spec;
	}

	private const string Song = """
		{
		  "format": "loom-song/1",
		  "name": "tune",
		  "about": "A song for the test",
		  "tempo": 118,
		  "scale": "F# minor",
		  "groups": [{ "name": "amp", "effects": [{ "fx": "distortion", "driveDb": 18, "toneHz": 3500, "cabinet": "fourByTwelve" }] }],
		  "tracks": [
		    { "name": "drums", "kit": "general", "level": -6, "humanise": { "ms": 4, "velocity": 0.1 } },
		    { "name": "lead", "patch": "pluck", "group": "amp", "octave": 5, "pan": -0.2, "macros": [{ "macro": "bright", "follow": "intensity", "from": 0.2, "to": 0.9 }] }
		  ],
		  "sections": [
		    {
		      "name": "verse", "bars": 2, "chords": "i | VI",
		      "parts": [
		        { "track": "drums", "beat": "eighths", "rows": { "kick": "x... x...", "snare": "..x. ..x." }, "endWith": { "beat": "sixteenths", "rows": { "snare": "x.x. x.x. xxxx XXXX" } } },
		        { "track": "lead", "name": "riff", "part": { "code": "Riff" }, "when": "intensity > 0.4 and not mood is Dark", "onPhrase": true }
		      ],
		      "sweeps": [{ "track": "lead", "macro": 0, "from": 0, "to": 1 }]
		    },
		    {
		      "name": "chorus", "bars": 2, "chords": "III | VII",
		      "parts": [
		        { "track": "lead", "strum": "eighths", "notes": "D.DU .UDU", "voicing": "triad", "when": "(intensity < 0.2 or mood is Calm)" }
		      ]
		    }
		  ],
		  "conduct": [{ "when": "intensity > 0.7 and mood is Calm", "then": "chorus" }, { "then": "verse" }],
		  "hooks": ["OnBuilt"]
		}
		""";

	private sealed class Text(string path, string text) : AdditionalText
	{
		public override string Path { get; } = path;

		public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
	}

	private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
	{
		public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
	}

	private sealed class Provider(AnalyzerConfigOptions global) : AnalyzerConfigOptionsProvider
	{
		public override AnalyzerConfigOptions GlobalOptions => global;

		public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => global;

		public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => global;
	}

	// The project compiled with the generator over its files: what it made, and every diagnostic
	private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult Run) Generate(params (string Path, string Text)[] files)
	{
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
			.Select(path => MetadataReference.CreateFromFile(path))
			.Append(MetadataReference.CreateFromFile(typeof(Patch).Assembly.Location))
			.Append(MetadataReference.CreateFromFile(typeof(Audio.AudioEffect).Assembly.Location));
		var parse = new CSharpParseOptions(LanguageVersion.Preview);
		var compilation = CSharpCompilation.Create("Tunes", [CSharpSyntaxTree.ParseText(Source, parse)], references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
		var project = Path.Combine(Path.GetTempPath(), "tunes");
		GeneratorDriver driver = CSharpGeneratorDriver.Create([new MusicGenerator().AsSourceGenerator()],
			[.. files.Select(f => (AdditionalText)new Text(Path.Combine(project, f.Path), f.Text))], parse,
			new Provider(new Options(new() { ["build_property.RootNamespace"] = "Tunes", ["build_property.ProjectDir"] = project + Path.DirectorySeparatorChar })));
		driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
		return (output, diagnostics, driver.GetRunResult());
	}

	private static System.Reflection.Assembly Emit(Compilation compilation)
	{
		using var image = new MemoryStream();
		var result = compilation.Emit(image);
		result.Success.ShouldBeTrue(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
		return System.Reflection.Assembly.Load(image.ToArray());
	}

	[Test]
	public void A_patch_file_and_a_song_file_are_made_into_the_code_that_builds_what_their_loaders_do()
	{
		var patchFile = PatchFile.Write(PluckSpec(), ["pick", "saw", "breath"]);
		var (output, diagnostics, run) = Generate(("Patches/pluck.patch.json", patchFile), ("Songs/tune.song.json", Song));
		diagnostics.ShouldBeEmpty();
		run.Results.Single().GeneratedSources.Select(s => s.HintName).ShouldBe(["Patches.g.cs", "Tune.g.cs"], ignoreOrder: true);
		var patchesCode = run.Results.Single().GeneratedSources.Single(s => s.HintName == "Patches.g.cs").SourceText.ToString();
		patchesCode.ShouldContain("public static Patch Pluck => Patch.Build(\"pluck\", p =>");
		patchesCode.ShouldContain("var pick = p.Sine(3f, Env.Decay(0.05f), 0.7f, velocity: 0.5f);");
		patchesCode.ShouldContain("// A plucked saw, for the test");
		patchesCode.ShouldContain("p.Macro(Macros.Bright, MacroTarget.FilterCutoff, 0.5f, 2f);");
		var songCode = run.Results.Single().GeneratedSources.Single(s => s.HintName == "Tune.g.cs").SourceText.ToString();
		songCode.ShouldContain("namespace Tunes.Songs;");
		songCode.ShouldContain("song.Conduct(now => now[Cues.Intensity] > 0.7f && now.Get(Cues.Mood) == global::Tunes.Mood.Calm ? chorus : verse);");
		songCode.ShouldContain(".When(Cues.Intensity.Above(0.4f) & !Cues.Mood.Is(global::Tunes.Mood.Dark))");
		songCode.ShouldContain("Fx.Distortion(driveDb: 18f, toneHz: 3500f, cabinet: global::Loom.Audio.Synth.Effects.Cabinet.FourByTwelve)");

		var assembly = Emit(output);
		var patch = (Patch)assembly.GetType("Tunes.Patches")!.GetProperty("Pluck", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
		PatchFile.Write(PatchSpec.From(patch), ["pick", "saw", "breath"]).ShouldBe(PatchFile.Write(PatchFile.Read(patchFile), ["pick", "saw", "breath"]).Replace("  \"about\": \"A plucked saw, for the test\",\n", ""));

		// The generated song and the same file loaded at run time, written out again: the same song
		var tune = assembly.GetType("Tunes.Songs.Tune")!;
		var generated = (Song)tune.GetMethod("Build")!.Invoke(null, null)!;
		var cues = (Type)assembly.GetType("Tunes.Cues")!;
		CueBase CueOf(string name) => (CueBase)cues.GetProperty(PatchCode.Pascal(name))!.GetValue(null)!;
		var loaded = SongFile.Load(Song, new SongBindings
		{
			Patch = name => name == "pluck" ? patch : null,
			Kit = name => (DrumKit?)assembly.GetType("Tunes.Kits")!.GetProperty(PatchCode.Pascal(name))?.GetValue(null),
			Cue = name => name is "intensity" or "mood" ? CueOf(name) : null,
			Macro = name => name == "bright" ? new Macro(0) : null,
			Part = name => (Part?)tune.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null),
			Hook = (name, song, sections) => tune.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [song, .. sections]),
		});
		var names = new SongNames { Patch = p => p.Name, Macro = m => m.Id == 0 ? "bright" : null };
		SongFile.Write(generated, names).ShouldBe(SongFile.Write(loaded, names));
		generated.Sections[0].Placements.Count.ShouldBe(3);
	}

	[Test]
	public void A_mistake_in_a_music_file_is_an_error_at_its_line()
	{
		var broken = Song.Replace("\"patch\": \"pluck\"", "\"patch\": \"nope\"", StringComparison.Ordinal);
		var (_, diagnostics, _) = Generate(("Songs/tune.song.json", broken));
		var error = diagnostics.ShouldHaveSingleItem();
		error.Id.ShouldBe("LOOMMUSIC001");
		error.GetMessage().ShouldContain("track 'lead' plays patch 'nope'");
		error.Location.GetLineSpan().StartLinePosition.Line.ShouldBe(broken.Split('\n').ToList().FindIndex(l => l.Contains("\"nope\"", StringComparison.Ordinal)));

		var unclosed = Generate(("Songs/tune.song.json", "{ \"format\": \"loom-song/1\", \"name\": \"x\"")).Diagnostics.ShouldHaveSingleItem();
		unclosed.GetMessage().ShouldContain("tune.song.json");
	}
}
