namespace BinaryVibrance.Sirrow.Synth.Chip;

internal sealed class KitManifestPiece
{
	public string Piece { get; set; } = "";
	public float GainDb { get; set; }
	public float Pan { get; set; }
	public int Choke { get; set; }
	public List<KitManifestLayer> Layers { get; set; } = [];
}
