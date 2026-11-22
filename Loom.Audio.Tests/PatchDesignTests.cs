

using Loom.Audio;
using Loom.Audio.Synth;
using Loom.Audio.Synth.Chip;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Loom.Tests;

/// <summary>
/// What a patch designer stands on: a patch as data and back (<see cref="PatchSpec"/>), as the C# that builds it
/// (<see cref="PatchCode"/>), swapped on a chip while it plays (<see cref="SynthChip.Replace"/>), and heard on a bus
/// (<see cref="AudioTap"/>).
/// </summary>
public class PatchDesignTests
{
	// Patches using every operator a patch can be written with, and every setting after them
	internal static IEnumerable<Patch> Patches()
	{
		yield return ChipRig.Tone;
		yield return MusicRig.Click;
		yield return Patch.Build("electric-piano", p =>
		{
			var strike = p.Sine(14f, Env.Decay(0.06f), 1f, velocity: 0.8f).KeyLevel(-4f);
			var tine = p.Sine(1f, Env.Adsr(0.002f, 2.2f, 0f, 0.2f), 0.35f, velocity: 0.3f).ModulatedBy(strike, 0.7f).KeyDecay(0.4f);
			var bark = p.Sine(1f, Env.Decay(0.5f), 1f, velocity: 1f).KeyLevel(-3f);
			var bar = p.Sine(1f, Env.Adsr(0.002f, 2.8f, 0f, 0.25f), 0.6f, velocity: 0.3f).ModulatedBy(bark, 1.3f).KeyDecay(0.4f);
			p.Out(tine, bar);
			p.Tremolo(4.5f, 0.15f);
			p.Send(ChipSend.Space, 0.2f);
			p.Gain(-3f);
		});
		yield return Patch.Build("picked-bass", p =>
		{
			var modulator = p.Sine(3f, Env.Decay(0.06f), 0.8f, velocity: 0.7f);
			p.Out(p.Sine(1f, Env.Adsr(0.002f, 0.9f, 0.3f, 0.08f)).Feedback(0.15f).ModulatedBy(modulator, 2f));
			p.Drive(0.2f);
			p.Mono();
		});
		yield return Patch.Build("flute", p =>
		{
			var breath = p.Level(Env.Adsr(0.04f, 0.2f, 0.8f, 0.12f), 1f);
			var noise = p.Noise(Env.Adsr(0.04f, 0.2f, 0.5f, 0.12f), 0.1f);
			p.Out(p.Tube().DrivenBy(breath, noise), noise.At(0.05f));
			p.Vibrato(5.2f, 0.12f, 0.25f);
			p.Filter(Filter.LowPass(5000f, keyTrack: 0.5f));
			p.Mono(0.04f);
			p.Send(ChipSend.Space, 0.3f);
			p.Macro(new Macro(0), MacroTarget.FilterCutoff, 0.6f, 1.6f);
		});
		yield return Patch.Build("acoustic-guitar", p =>
		{
			var plucked = p.String(1f, decaySeconds: 5.5f, damping: 0.4f, pick: 0.165f).KeyDecay(0.1f);
			var knock = p.Sine(1f, Env.Decay(0.004f), 1f, velocity: 1f).Fixed(300f);
			var air = p.Modes(Mode.Fixed(96.5f, 0.8f)).DrivenBy(knock).KeyLevel(-8f);
			var presence = p.Modes(Mode.Fixed(3200f, 0.006f), Mode.At(9.5f, 0.004f, 0.9f)).DrivenBy(plucked);
			p.Out(plucked.At(0.8f), air.At(0.009f), presence.At(0.045f, -0.3f));
			p.Filter(Filter.HighPass(120f, q: 1f));
			p.Macro(new Macro(0), MacroTarget.OperatorLevel, 0.5f, 1.6f, presence);
		});
		yield return Patch.Build("reese", p =>
		{
			var held = Env.Adsr(0.01f, 0.3f, 0.9f, 0.1f);
			p.Out(
				p.Wave(Waveform.Saw, 1f, held, 0.45f).Detune(-19f).At(1f, -0.2f),
				p.Wave(Waveform.Saw, 1f, held, 0.45f).Detune(18f).Phase(0.4f).At(1f, 0.2f),
				p.Wave(Waveform.Pulse, 0.5f, held, 0.3f, pulseWidth: 0.35f),
				p.Noise(Env.Decay(0.02f), 0.2f, velocity: 0.5f, pink: true));
			p.Filter(Filter.LowPass(170f, 1.5f, keyTrack: 0.2f, velocity: 0.3f, envelopeOctaves: 1.2f, envelope: Env.Adsr(0.02f, 0.35f, 0.3f, 0.1f)));
			p.PitchDrop(7f, 0.04f);
			p.Macro(new Macro(2), MacroTarget.FilterOctaves, 0f, 2f);
			p.Drive(0.45f);
			p.Send(ChipSend.Echo, 0.25f);
			p.Send(ChipSend.Key, 1f);
			p.Pan(0.1f);
			p.Gain(-2f);
		});
	}

	// Equal as data: a record compares its arrays by reference, so they are compared by their items
	internal static void ShouldEqual(Patch actual, Patch expected)
	{
		(actual with { Operators = expected.Operators, Outputs = expected.Outputs, Macros = expected.Macros }).ShouldBe(expected);
		actual.Outputs.ShouldBe(expected.Outputs);
		actual.Macros.ShouldBe(expected.Macros);
		actual.Operators.Length.ShouldBe(expected.Operators.Length);
		for (var i = 0; i < expected.Operators.Length; i++)
		{
			var a = actual.Operators[i];
			var e = expected.Operators[i];
			(a with { Modulators = e.Modulators, Drivers = e.Drivers, Modes = e.Modes }).ShouldBe(e, $"operator {i}");
			a.Modulators.ShouldBe(e.Modulators, $"operator {i}'s modulators");
			a.Drivers.ShouldBe(e.Drivers, $"operator {i}'s drivers");
			a.Modes.ShouldBe(e.Modes, $"operator {i}'s modes");
		}
	}

	[Test]
	public void A_patch_made_data_and_built_again_is_the_same_patch()
	{
		foreach (var patch in Patches())
			ShouldEqual(PatchSpec.From(patch).ToPatch(), patch);
	}

	[Test]
	public void A_spec_changed_builds_the_change_and_its_clone_is_its_own()
	{
		var spec = PatchSpec.From(Patches().First(p => p.Name == "picked-bass"));
		var copy = spec.Clone();
		spec.Operators[1] = spec.Operators[1] with { Feedback = 0.4f };
		spec.GainDb = -6f;
		var built = spec.ToPatch();
		built.Operators[1].Feedback.ShouldBe(0.4f);
		built.GainDb.ShouldBe(-6f);
		copy.Operators[1].Feedback.ShouldBe(0.15f);
	}

	[Test]
	public void A_spec_routed_from_a_later_operator_is_refused_as_Build_refuses_it()
	{
		var spec = PatchSpec.From(ChipRig.Tone);
		spec.Operators.Insert(0, spec.Operators[0] with { Modulators = [new Modulation(1, 1f)] });
		Should.Throw<PatchException>(spec.ToPatch).Message.ShouldContain("only by one before it");

		var silent = PatchSpec.From(ChipRig.Tone);
		silent.Outputs.Clear();
		Should.Throw<PatchException>(silent.ToPatch).Message.ShouldContain("nothing is heard");
	}

	[Test]
	public void The_code_written_for_a_patch_builds_the_same_patch()
	{
		var patches = Patches().ToArray();
		var properties = patches.Select(p => PatchCode.Write(p, names: null)).ToArray();
		var source = "using Loom.Audio.Synth.Chip;\nusing Loom.Audio.Synth.Dsp;\nnamespace Written;\npublic static class Patches\n{\n"
			+ string.Join("\n\n", properties) + "\n}\n";
		var assembly = Compile(source);
		var written = assembly.GetType("Written.Patches")!;
		foreach (var patch in patches)
		{
			var built = (Patch)written.GetProperty(PatchCode.Pascal(patch.Name))!.GetValue(null)!;
			ShouldEqual(built, patch);
		}
	}

	[Test]
	public void The_code_reads_as_a_person_would_write_it()
	{
		var code = PatchCode.Write(Patches().First(p => p.Name == "picked-bass"), names: ["modulator"], indent: "    ");
		code.ShouldBe("""
			public static Patch PickedBass => Patch.Build("picked-bass", p =>
			{
			    var modulator = p.Sine(3f, Env.Decay(0.06f), 0.8f, velocity: 0.7f);
			    p.Out(p.Sine(1f, Env.Adsr(0.002f, 0.9f, 0.3f, 0.08f)).Feedback(0.15f).ModulatedBy(modulator, 2f));
			    p.Drive(0.2f);
			    p.Mono();
			});
			""".ReplaceLineEndings("\n"));
	}

	[Test]
	public void A_replaced_instruments_next_note_plays_the_new_patch_and_keeps_its_macros()
	{
		using var rig = new ChipRig();
		var low = Patch.Build("low", p => p.Out(p.Sine(1f, Env.Gate)));
		var high = Patch.Build("high", p => p.Out(p.Sine(2f, Env.Gate)));
		var instrument = rig.Chip.AddInstrument(low);
		rig.Chip.SetMacro(instrument, new Macro(3), 0.7f);
		rig.Chip.Play(instrument, 57f, 0.8f, 0.3f);
		rig.Seconds(0.4);
		rig.Chip.Replace(instrument, high);
		rig.Chip.Play(instrument, 57f, 0.8f, 0.3f);
		rig.Seconds(0.4);

		rig.Pitch(0.05, 0.2).ShouldBe(220, 3);
		rig.Pitch(0.45, 0.2).ShouldBe(440, 3);
		rig.Chip.Core.Instrument(instrument.Index)!.Macros[3].ShouldBe(0.7f);
		Should.Throw<ArgumentException>(() => rig.Chip.Replace(rig.Chip.AddKit(DrumKit.FromPatches("kit", (Kit.Kick, low, 0))), high));
	}

	[Test]
	public void A_tap_hands_on_what_passes_through_it_and_changes_none_of_it()
	{
		var tap = new AudioTap(1024);
		tap.Prepare(48_000);
		var left = new float[256];
		var right = new float[256];
		for (var i = 0; i < 256; i++)
		{
			left[i] = i / 256f;
			right[i] = -i / 512f;
		}
		var before = left.ToArray();
		tap.Run(left, right);
		left.ShouldBe(before);

		var into = new float[300];
		tap.Read(into).ShouldBe(256);
		into[10].ShouldBe(0.5f * (10 / 256f - 10 / 512f));
		for (var block = 0; block < 5; block++)
			tap.Run(new float[256], new float[256]);
		tap.Read(into).ShouldBe(300);
		into.ShouldAllBe(x => x == 0f);
	}

	private static System.Reflection.Assembly Compile(string source)
	{
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
			.Select(path => MetadataReference.CreateFromFile(path))
			.Append(MetadataReference.CreateFromFile(typeof(Patch).Assembly.Location));
		var compilation = CSharpCompilation.Create("Written", [CSharpSyntaxTree.ParseText(source)], references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		using var image = new MemoryStream();
		var result = compilation.Emit(image);
		result.Success.ShouldBeTrue(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) + "\n" + source);
		return System.Reflection.Assembly.Load(image.ToArray());
	}
}
