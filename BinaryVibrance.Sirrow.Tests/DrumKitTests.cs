using System.Text;

using BinaryVibrance.Sirrow.Synth;
using BinaryVibrance.Sirrow.Synth.Chip;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>Samples picked by note, velocity and turn; kits loaded from a manifest, and the manifests refused.</summary>
public class DrumKitTests
{
	private static SampleData Data(float value, int frames = 100) => new(Enumerable.Repeat(value, frames).ToArray(), 48_000);

	[Test]
	public void A_sample_set_picks_by_note_velocity_and_turn()
	{
		var low = new SampleZone(Data(0.1f), RootNote: 48, LowNote: 0, HighNote: 59);
		var highSoft = new SampleZone(Data(0.2f), RootNote: 72, LowNote: 60, HighNote: 127, HighVelocity: 0.5f);
		var highHardA = new SampleZone(Data(0.3f), RootNote: 72, LowNote: 60, HighNote: 127, LowVelocity: 0.5f);
		var highHardB = new SampleZone(Data(0.4f), RootNote: 72, LowNote: 60, HighNote: 127, LowVelocity: 0.5f);
		var set = new SampleSet(low, highSoft, highHardA, highHardB);

		set.Pick(50, 0.8f, 0).ShouldBe(low);
		set.Pick(70, 0.2f, 0).ShouldBe(highSoft);
		set.Pick(70, 0.9f, 0).ShouldBe(highHardA);
		set.Pick(70, 0.9f, 1).ShouldBe(highHardB);
		set.Pick(70, 0.9f, 2).ShouldBe(highHardA);
		// A velocity at a zone's top belongs to it, and here to the next zone too: the turn picks among both
		set.Pick(70, 0.5f, 0).ShouldBe(highSoft);
		set.Pick(70, 0.5f, 1).ShouldBe(highHardA);
	}

	[Test]
	public void With_no_zone_holding_it_the_nearest_root_plays()
	{
		var a = new SampleZone(Data(0.1f), RootNote: 40, LowNote: 40, HighNote: 41);
		var b = new SampleZone(Data(0.2f), RootNote: 80, LowNote: 80, HighNote: 81);
		var set = new SampleSet(a, b);

		set.Pick(50, 0.5f, 0).ShouldBe(a);
		set.Pick(70, 0.5f, 0).ShouldBe(b);
	}

	private static byte[] Wav(float value, int frames = 480)
	{
		using var stream = new MemoryStream();
		WavWriter.Write(stream, Enumerable.Repeat(value, frames).ToArray(), 1, 48_000);
		return stream.ToArray();
	}

	private static Func<string, byte[]> Files(string manifest, params string[] wavs)
	{
		var files = new Dictionary<string, byte[]> { ["kits/rock/kit.json"] = Encoding.UTF8.GetBytes(manifest) };
		foreach (var wav in wavs)
			files["kits/rock/" + wav] = Wav(0.25f);
		return path => files.TryGetValue(path, out var bytes) ? bytes : throw new FileNotFoundException($"No file '{path}'.", path);
	}

	private const string Good = """
		{
		  "name": "rock",
		  "source": "made for a test",
		  "pieces": [
		    { "piece": "kick", "gainDb": -2, "pan": 0, "choke": 0,
		      "layers": [ { "upTo": 0.35, "takes": ["kick_1a.wav", "kick_1b.wav"] },
		                  { "upTo": 1.0,  "takes": ["kick_2a.wav"] } ] },
		    { "piece": "hat-closed", "choke": 1, "layers": [ { "upTo": 1.0, "takes": ["hat_1a.wav"] } ] },
		    { "piece": "cowbell", "layers": [ { "upTo": 1.0, "takes": ["cowbell_1a.wav"] } ] }
		  ]
		}
		""";

	[Test]
	public void A_kit_loads_the_pieces_layers_and_takes_its_manifest_lists()
	{
		var kit = DrumKit.Load(Files(Good, "kick_1a.wav", "kick_1b.wav", "kick_2a.wav", "hat_1a.wav", "cowbell_1a.wav"), "kits/rock/kit.json");

		kit.Name.ShouldBe("rock");
		kit.Pieces.Count.ShouldBe(3);
		var kick = kit.Pieces[kit.IndexOf(Kit.Kick)];
		kick.GainDb.ShouldBe(-2f);
		kick.Layers.Length.ShouldBe(2);
		kick.Layers[0].UpToVelocity.ShouldBe(0.35f);
		kick.Layers[0].Takes.Length.ShouldBe(2);
		kick.Layers[1].Takes.Length.ShouldBe(1);
		kick.Layers[0].Takes[0].Samples.Length.ShouldBe(480);
		kick.Layers[0].Takes[0].Samples[10].ShouldBe(0.25f);
		kit.Pieces[kit.IndexOf(Kit.HatClosed)].ChokeGroup.ShouldBe(1);
		kit.IndexOf(new DrumPiece("cowbell")).ShouldBe(2);
		kit.IndexOf(Kit.Crash).ShouldBe(-1);
	}

	[Test]
	public void A_bad_manifest_is_refused_naming_it_and_the_piece()
	{
		void Refused(string manifest, params string[] wavs)
		{
			var thrown = Should.Throw<InvalidDataException>(() => DrumKit.Load(Files(manifest, wavs), "kits/rock/kit.json"));
			thrown.Message.ShouldContain("kits/rock/kit.json");
			thrown.Message.ShouldContain("kick");
		}

		Refused("""{ "name": "x", "pieces": [ { "piece": "kick", "layers": [ { "upTo": 1, "takes": ["missing.wav"] } ] } ] }""");
		Refused("""{ "name": "x", "pieces": [ { "piece": "kick", "layers": [ { "upTo": 1, "takes": [] } ] } ] }""");
		Refused("""{ "name": "x", "pieces": [ { "piece": "kick", "layers": [ { "upTo": 0.8, "takes": ["a.wav"] }, { "upTo": 0.5, "takes": ["a.wav"] } ] } ] }""", "a.wav");
	}

	[Test]
	public void A_layer_with_no_takes_is_refused() =>
		Should.Throw<ArgumentException>(() => new DrumKit("bad", new DrumKitPiece(Kit.Snare,
			[new DrumLayer(0.5f, [ChipRig.SineSample(500, 0.1)]), new DrumLayer(1f, [])]))).Message.ShouldContain("no takes");

	[Test]
	public void Layers_not_rising_in_velocity_are_refused() =>
		Should.Throw<ArgumentException>(() => new DrumKit("bad", new DrumKitPiece(Kit.Snare,
			[new DrumLayer(1f, [ChipRig.SineSample(500, 0.1)]), new DrumLayer(0.5f, [ChipRig.SineSample(600, 0.1)])]))).Message.ShouldContain("softest first");
}
