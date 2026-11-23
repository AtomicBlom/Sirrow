namespace BinaryVibrance.Sirrow.Decoding.Vorbis;

/// <summary>
/// A mapping: which floor and residue each channel uses (through submaps), and which channel pairs are coupled
/// (coded as magnitude and angle, and turned back into the two channels after the residue).
/// </summary>
internal sealed class VorbisMapping
{
	private VorbisMapping(int[] magnitude, int[] angle, int[] mux, int[] submapFloor, int[] submapResidue)
	{
		Magnitude = magnitude;
		Angle = angle;
		Mux = mux;
		SubmapFloor = submapFloor;
		SubmapResidue = submapResidue;
		SubmapChannels = new int[submapFloor.Length][];
		SubmapSkip = new bool[submapFloor.Length][];
		for (var s = 0; s < submapFloor.Length; s++)
		{
			var submap = s;
			SubmapChannels[s] = [.. Enumerable.Range(0, mux.Length).Where(c => mux[c] == submap)];
			SubmapSkip[s] = new bool[SubmapChannels[s].Length];
		}
	}

	/// <summary>The coupling steps' magnitude channels, applied in reverse after the residue.</summary>
	public int[] Magnitude { get; }

	public int[] Angle { get; }

	/// <summary>Each channel's submap.</summary>
	public int[] Mux { get; }

	public int[] SubmapFloor { get; }

	public int[] SubmapResidue { get; }

	/// <summary>Each submap's channels, in order: the bundle its residue decodes.</summary>
	public int[][] SubmapChannels { get; }

	/// <summary>Scratch for each submap: which of its channels the residue skips.</summary>
	public bool[][] SubmapSkip { get; }

	/// <summary>Reads a type 0 mapping (the only type) from the setup header.</summary>
	public static VorbisMapping Read(VorbisBitReader reader, int channels, int floors, int residues)
	{
		var submaps = reader.ReadBit() ? (int)reader.ReadBits(4) + 1 : 1;

		int[] magnitude = [];
		int[] angle = [];
		if (reader.ReadBit())
		{
			var steps = (int)reader.ReadBits(8) + 1;
			magnitude = new int[steps];
			angle = new int[steps];
			var bits = VorbisBitReader.Ilog(channels - 1);
			for (var i = 0; i < steps; i++)
			{
				magnitude[i] = (int)reader.ReadBits(bits);
				angle[i] = (int)reader.ReadBits(bits);
				if (magnitude[i] == angle[i] || magnitude[i] >= channels || angle[i] >= channels)
					throw new InvalidDataException("A Vorbis mapping couples a channel that doesn't exist, or a channel with itself: the setup header is damaged.");
			}
		}

		if (reader.ReadBits(2) != 0)
			throw new InvalidDataException("A Vorbis mapping's reserved bits are set: the setup header is damaged.");

		var mux = new int[channels];
		if (submaps > 1)
		{
			for (var c = 0; c < channels; c++)
			{
				mux[c] = (int)reader.ReadBits(4);
				if (mux[c] >= submaps)
					throw new InvalidDataException("A Vorbis mapping puts a channel in a submap it doesn't have: the setup header is damaged.");
			}
		}

		var submapFloor = new int[submaps];
		var submapResidue = new int[submaps];
		for (var s = 0; s < submaps; s++)
		{
			reader.ReadBits(8); // the time configuration, unused
			submapFloor[s] = (int)reader.ReadBits(8);
			submapResidue[s] = (int)reader.ReadBits(8);
			if (submapFloor[s] >= floors || submapResidue[s] >= residues)
				throw new InvalidDataException("A Vorbis mapping names a floor or residue that doesn't exist: the setup header is damaged.");
		}
		if (reader.EndOfPacket)
			throw new InvalidDataException("The Vorbis setup header ends inside a mapping: the file is damaged.");
		return new VorbisMapping(magnitude, angle, mux, submapFloor, submapResidue);
	}
}
