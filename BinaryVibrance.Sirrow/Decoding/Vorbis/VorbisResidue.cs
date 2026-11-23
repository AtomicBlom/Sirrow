namespace BinaryVibrance.Sirrow.Decoding.Vorbis;

/// <summary>
/// A residue: the spectrum's fine detail, which the floor scales. Coded in partitions, each classified, and each class
/// coded in up to eight passes of vector-quantized codebooks. Type 0 interleaves a partition's vector values, type 1
/// lays them end to end, and type 2 codes every channel as one vector interleaved sample by sample (how coupled stereo
/// is coded).
/// </summary>
internal sealed class VorbisResidue
{
	private readonly int _type;
	private readonly int _begin;
	private readonly int _end;
	private readonly int _partitionSize;
	private readonly int _classifications;
	private readonly int _classbook;
	private readonly int[][] _books; // [class][pass]: the codebook, or -1 when the pass codes nothing for the class
	private readonly int[][] _classes; // scratch: each vector's partitions' classes

	private VorbisResidue(int type, int begin, int end, int partitionSize, int classifications, int classbook, int[][] books, int[][] classes)
	{
		_type = type;
		_begin = begin;
		_end = end;
		_partitionSize = partitionSize;
		_classifications = classifications;
		_classbook = classbook;
		_books = books;
		_classes = classes;
	}

	/// <summary>Reads a residue of <paramref name="type"/> from the setup header, sized for <paramref name="channels"/> channels of spectra up to <paramref name="maxHalf"/> lines.</summary>
	public static VorbisResidue Read(VorbisBitReader reader, int type, VorbisCodebook[] codebooks, int channels, int maxHalf)
	{
		var begin = (int)reader.ReadBits(24);
		var end = (int)reader.ReadBits(24);
		var partitionSize = (int)reader.ReadBits(24) + 1;
		var classifications = (int)reader.ReadBits(6) + 1;
		var classbook = CheckBook((int)reader.ReadBits(8), codebooks, needsVectors: false);

		var cascade = new int[classifications];
		for (var c = 0; c < classifications; c++)
		{
			var low = (int)reader.ReadBits(3);
			var high = reader.ReadBit() ? (int)reader.ReadBits(5) : 0;
			cascade[c] = high * 8 + low;
		}
		var books = new int[classifications][];
		for (var c = 0; c < classifications; c++)
		{
			books[c] = new int[8];
			for (var pass = 0; pass < 8; pass++)
				books[c][pass] = (cascade[c] & (1 << pass)) != 0 ? CheckBook((int)reader.ReadBits(8), codebooks, needsVectors: true) : -1;
		}
		if (reader.EndOfPacket)
			throw new InvalidDataException("The Vorbis setup header ends inside a residue: the file is damaged.");

		// Type 2 decodes all its channels as one vector, as long as all of them
		var vectors = type == 2 ? 1 : channels;
		var size = type == 2 ? maxHalf * channels : maxHalf;
		var partitions = Math.Max(0, Math.Min(end, size) - Math.Min(begin, size)) / partitionSize;
		var classes = new int[vectors][];
		for (var v = 0; v < vectors; v++)
			classes[v] = new int[partitions + codebooks[classbook].Dimensions];
		return new VorbisResidue(type, begin, end, partitionSize, classifications, classbook, books, classes);
	}

	/// <summary>
	/// Adds the packet's residue for <paramref name="channels"/> (indices into <paramref name="spectra"/>) into their
	/// spectra, <paramref name="half"/> lines each. A channel whose <paramref name="skip"/> is set is left alone. The end of
	/// the packet ends the residue, the rest zero, as the spec has it.
	/// </summary>
	public void Decode(VorbisBitReader reader, VorbisCodebook[] books, float[][] spectra, int[] channels, bool[] skip, int half)
	{
		if (_type != 2)
		{
			Decode(reader, books, spectra, channels, skip, channels.Length, half);
			return;
		}

		// Type 2 is all or nothing: decoded unless every channel is to be skipped
		var any = false;
		foreach (var s in skip)
			any |= !s;
		if (any)
			Decode(reader, books, spectra, channels, null, 1, half * channels.Length);
	}

	private void Decode(VorbisBitReader reader, VorbisCodebook[] books, float[][] spectra, int[] channels, bool[]? skip, int vectors, int size)
	{
		var limitBegin = Math.Min(_begin, size);
		var limitEnd = Math.Min(_end, size);
		var classbook = books[_classbook];
		var perWord = classbook.Dimensions;
		var partitions = (limitEnd - limitBegin) / _partitionSize;
		if (partitions <= 0)
			return;

		for (var pass = 0; pass < 8; pass++)
		{
			var p = 0;
			while (p < partitions)
			{
				if (pass == 0)
				{
					for (var v = 0; v < vectors; v++)
					{
						if (skip is not null && skip[v])
							continue;
						var word = classbook.Decode(reader);
						if (word < 0)
							return;
						var classes = _classes[v];
						for (var i = perWord - 1; i >= 0; i--)
						{
							classes[p + i] = word % _classifications;
							word /= _classifications;
						}
					}
				}

				for (var i = 0; i < perWord && p < partitions; i++, p++)
				{
					for (var v = 0; v < vectors; v++)
					{
						if (skip is not null && skip[v])
							continue;
						var book = _books[_classes[v][p]][pass];
						if (book >= 0 && !DecodePartition(reader, books[book], spectra, channels, v, limitBegin + p * _partitionSize))
							return;
					}
				}
			}
		}
	}

	private bool DecodePartition(VorbisBitReader reader, VorbisCodebook book, float[][] spectra, int[] channels, int vector, int offset)
	{
		var dimensions = book.Dimensions;
		if (_type == 0)
		{
			var target = spectra[channels[vector]];
			var step = _partitionSize / dimensions;
			for (var k = 0; k < step; k++)
			{
				var entry = book.Decode(reader);
				if (entry < 0)
					return false;
				var values = book.Vector(entry);
				for (var d = 0; d < dimensions; d++)
					target[offset + k + d * step] += values[d];
			}
			return true;
		}

		var position = offset;
		var end = offset + _partitionSize;
		while (position < end)
		{
			var entry = book.Decode(reader);
			if (entry < 0)
				return false;
			var values = book.Vector(entry);
			for (var d = 0; d < dimensions && position < end; d++, position++)
			{
				if (_type == 1)
					spectra[channels[vector]][position] += values[d];
				else
					spectra[channels[position % channels.Length]][position / channels.Length] += values[d];
			}
		}
		return true;
	}

	private static int CheckBook(int book, VorbisCodebook[] codebooks, bool needsVectors)
	{
		if (book >= codebooks.Length)
			throw new InvalidDataException($"A Vorbis residue names codebook {book} of {codebooks.Length}: the setup header is damaged.");
		if (needsVectors && !codebooks[book].HasVectors)
			throw new InvalidDataException("A Vorbis residue codes with a codebook that has no vectors: the setup header is damaged.");
		return book;
	}
}
