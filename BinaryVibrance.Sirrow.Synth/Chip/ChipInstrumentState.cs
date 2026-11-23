namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>
/// An instrument as the chip's core holds it: made whole on the game thread (so the audio thread allocates nothing to
/// add it), then owned by the core. A patch, or a kit with a one-operator patch made for each sampled piece.
/// </summary>
internal sealed class ChipInstrumentState
{
	public ChipInstrumentState(Patch patch, int group)
	{
		Patch = patch;
		Group = group;
	}

	public ChipInstrumentState(DrumKit kit, int group)
	{
		Kit = kit;
		Group = group;
		PiecePatches = new Patch[kit.Pieces.Count];
		PieceTurns = new int[kit.Pieces.Count];
		for (var i = 0; i < PiecePatches.Length; i++)
		{
			var piece = kit.Pieces[i];
			PiecePatches[i] = piece.Patch ?? SampledPiece(kit, piece);
		}
	}

	public Patch? Patch { get; }
	public DrumKit? Kit { get; }
	public int Group { get; }
	public float[] Macros { get; } = new float[Chip.Macro.Count];
	public Patch[] PiecePatches { get; } = [];
	public int[] PieceTurns { get; } = [];
	public int NoteTurn;    // a counter stepped per note, for a sample operator's alternate takes
	public float Gain = 1f;   // a linear gain over every voice of it: a song track's level
	public bool Muted;        // its notes and hits are ignored while set

	// A sampled piece as a patch: one one-shot sample operator (its take chosen at the hit), the piece's gain and pan.
	// The velocity is left to the voice's own velocity squared, since the layers carry the dynamics
	private static Patch SampledPiece(DrumKit kit, DrumKitPiece piece)
	{
		var zones = new List<SampleZone>();
		foreach (var layer in piece.Layers)
			foreach (var take in layer.Takes)
				zones.Add(new SampleZone(take));
		var set = new SampleSet([.. zones]);
		return Patch.Build($"{kit.Name}/{piece.Piece.Id}", p =>
		{
			p.Out(p.Sample(set, velocity: 0f, oneShot: true));
			p.Gain(piece.GainDb);
			p.Pan(piece.Pan);
		});
	}

	// The take a hit at velocity plays: the first layer up to it (the last if none), then the next take in turn
	public SampleData PickTake(int pieceIndex, float velocity)
	{
		var layers = Kit!.Pieces[pieceIndex].Layers;
		var layer = layers[^1];
		foreach (var candidate in layers)
			if (candidate.UpToVelocity >= velocity)
			{
				layer = candidate;
				break;
			}
		var turn = PieceTurns[pieceIndex]++;
		return layer.Takes[(int)((uint)turn % (uint)layer.Takes.Length)];
	}
}
