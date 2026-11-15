using System.Text.Json.Serialization;

namespace Loom.Audio.Synth.Chip;

// A drum kit's kit.json, as kit-cut writes it and DrumKit.Load reads it
internal sealed class KitManifest
{
	public string Name { get; set; } = "";
	public string? Source { get; set; }
	public List<KitManifestPiece> Pieces { get; set; } = [];
}

internal sealed class KitManifestPiece
{
	public string Piece { get; set; } = "";
	public float GainDb { get; set; }
	public float Pan { get; set; }
	public int Choke { get; set; }
	public List<KitManifestLayer> Layers { get; set; } = [];
}

internal sealed class KitManifestLayer
{
	public float UpTo { get; set; }
	public List<string> Takes { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(KitManifest))]
internal sealed partial class KitManifestJson : JsonSerializerContext;
