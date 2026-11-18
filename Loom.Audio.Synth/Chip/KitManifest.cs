namespace Loom.Audio.Synth.Chip;

// A drum kit's kit.json, as kit-cut writes it and DrumKit.Load reads it
internal sealed class KitManifest
{
	public string Name { get; set; } = "";
	public string? Source { get; set; }
	public List<KitManifestPiece> Pieces { get; set; } = [];
}
