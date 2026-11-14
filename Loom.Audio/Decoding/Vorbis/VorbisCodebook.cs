namespace Loom.Audio.Decoding.Vorbis;

/// <summary>
/// A Vorbis codebook: a Huffman code over its entries, and for a codebook with a lookup, the vector each entry stands
/// for. Decoding looks the next ten bits up in a table, which covers almost every codeword, and walks a tree for the
/// rest. Everything is built when the setup header is read; decoding allocates nothing.
/// </summary>
internal sealed class VorbisCodebook
{
	private const int FastBits = 10;

	private readonly byte[] _lengths;
	private readonly int[] _fast; // by the next FastBits bits as read: entry << 5 | length, or -1 for a longer codeword
	private readonly int[] _tree; // two slots a node: 0 empty, > 0 the child node, < 0 the leaf ~entry
	private readonly int _single = -1; // the entry of a codebook with only one, which takes its length in bits whatever they are
	private readonly float[]? _vectors;

	private VorbisCodebook(int dimensions, int entries, byte[] lengths, float[]? vectors)
	{
		Dimensions = dimensions;
		Entries = entries;
		_lengths = lengths;
		_vectors = vectors;

		var used = 0;
		for (var i = 0; i < entries; i++)
		{
			if (lengths[i] == 0)
				continue;
			used++;
			_single = i;
		}
		if (used != 1)
			_single = -1;

		var codes = MakeCodewords(lengths, used);
		_fast = new int[1 << FastBits];
		Array.Fill(_fast, -1);
		_tree = new int[(used + 33) * 2];
		var nodes = 1;
		for (var entry = 0; entry < entries; entry++)
		{
			int length = lengths[entry];
			if (length == 0)
				continue;
			var code = codes[entry];
			if (length <= FastBits)
			{
				// The first bit read is the codeword's most significant: index the table by the codeword reversed
				var reversed = 0;
				for (var b = 0; b < length; b++)
					reversed |= (int)((code >> (length - 1 - b)) & 1) << b;
				for (var index = reversed; index < _fast.Length; index += 1 << length)
					_fast[index] = entry << 5 | length;
			}

			var node = 0;
			for (var b = length - 1; b >= 0; b--)
			{
				var slot = node * 2 + (int)((code >> b) & 1);
				if (b == 0)
				{
					_tree[slot] = ~entry;
					break;
				}
				if (_tree[slot] < 0)
					throw new InvalidDataException("A Vorbis codebook's codewords overlap: the setup header is damaged.");
				if (_tree[slot] == 0)
				{
					if (nodes * 2 >= _tree.Length)
						throw new InvalidDataException("A Vorbis codebook's Huffman tree is malformed: the setup header is damaged.");
					_tree[slot] = nodes++;
				}
				node = _tree[slot];
			}
		}
	}

	/// <summary>How many values each entry's vector holds.</summary>
	public int Dimensions { get; }

	public int Entries { get; }

	/// <summary>Whether the entries stand for vectors (a codebook residues can use), not just numbers.</summary>
	public bool HasVectors => _vectors is not null;

	/// <summary>The vector <paramref name="entry"/> stands for.</summary>
	public ReadOnlySpan<float> Vector(int entry) => _vectors.AsSpan(entry * Dimensions, Dimensions);

	/// <summary>The next entry in the packet, or -1 at the end of the packet.</summary>
	public int Decode(VorbisBitReader reader)
	{
		if (_single >= 0)
		{
			reader.Skip(_lengths[_single]);
			return reader.EndOfPacket ? -1 : _single;
		}

		var bits = reader.Peek(FastBits, out var available);
		var fast = _fast[bits];
		if (fast >= 0)
		{
			var length = fast & 31;
			reader.Skip(length);
			return length > available ? -1 : fast >> 5;
		}

		var node = 0;
		while (true)
		{
			var bit = (int)reader.ReadBits(1);
			if (reader.EndOfPacket)
				return -1;
			var next = _tree[node * 2 + bit];
			if (next < 0)
				return ~next;
			if (next == 0)
				throw new InvalidDataException("A Vorbis packet holds a codeword its codebook doesn't have: the file is damaged.");
			node = next;
		}
	}

	/// <summary>Reads a codebook from the setup header.</summary>
	public static VorbisCodebook Read(VorbisBitReader reader)
	{
		if (reader.ReadBits(24) != 0x564342)
			throw new InvalidDataException("A Vorbis codebook doesn't start with its sync pattern: the setup header is damaged.");
		var dimensions = (int)reader.ReadBits(16);
		var entries = (int)reader.ReadBits(24);
		var lengths = new byte[entries];

		if (!reader.ReadBit())
		{
			var sparse = reader.ReadBit();
			for (var i = 0; i < entries; i++)
			{
				if (!sparse || reader.ReadBit())
					lengths[i] = (byte)(reader.ReadBits(5) + 1);
			}
		}
		else
		{
			var current = 0;
			var length = (int)reader.ReadBits(5) + 1;
			while (current < entries)
			{
				var count = (int)reader.ReadBits(VorbisBitReader.Ilog(entries - current));
				if (current + count > entries || length > 32)
					throw new InvalidDataException("A Vorbis codebook's ordered lengths overrun it: the setup header is damaged.");
				lengths.AsSpan(current, count).Fill((byte)length);
				current += count;
				length++;
			}
		}

		var lookup = (int)reader.ReadBits(4);
		float[]? vectors = null;
		if (lookup is 1 or 2)
		{
			if (dimensions == 0)
				throw new InvalidDataException("A Vorbis codebook has a vector lookup but no dimensions: the setup header is damaged.");
			var minimum = Float32Unpack(reader.ReadBits(32));
			var delta = Float32Unpack(reader.ReadBits(32));
			var valueBits = (int)reader.ReadBits(4) + 1;
			var sequence = reader.ReadBit();
			var values = lookup == 1 ? Lookup1Values(entries, dimensions) : (long)entries * dimensions;
			if (values > 1 << 24 || (long)entries * dimensions > 1 << 24)
				throw new InvalidDataException("A Vorbis codebook's lookup is too large: the setup header is damaged.");
			var multiplicands = new int[values];
			for (var i = 0; i < values; i++)
				multiplicands[i] = (int)reader.ReadBits(valueBits);
			vectors = BuildVectors(lookup, entries, dimensions, multiplicands, minimum, delta, sequence);
		}
		else if (lookup != 0)
			throw new InvalidDataException($"A Vorbis codebook has lookup type {lookup}; only 0, 1 and 2 exist.");

		if (reader.EndOfPacket)
			throw new InvalidDataException("The Vorbis setup header ends inside a codebook: the file is damaged.");
		return new VorbisCodebook(dimensions, entries, lengths, vectors);
	}

	// The spec's codeword assignment: each entry, in order, takes the lowest free codeword of its length
	private static uint[] MakeCodewords(byte[] lengths, int used)
	{
		var codes = new uint[lengths.Length];
		Span<uint> marker = stackalloc uint[33];
		marker.Clear();
		for (var i = 0; i < lengths.Length; i++)
		{
			int length = lengths[i];
			if (length == 0)
				continue;
			var entry = marker[length];
			if (length < 32 && (entry >> length) != 0)
				throw new InvalidDataException("A Vorbis codebook has more codewords than its lengths allow: the setup header is damaged.");
			codes[i] = entry;

			// Take the node: move this length's marker on, and the shorter ones above it where they pointed at it
			for (var j = length; j > 0; j--)
			{
				if ((marker[j] & 1) != 0)
				{
					if (j == 1)
						marker[1]++;
					else
						marker[j] = marker[j - 1] << 1;
					break;
				}
				marker[j]++;
			}

			// The longer markers hung from the node just taken: hang them from the next one
			for (var j = length + 1; j < 33; j++)
			{
				if ((marker[j] >> 1) != entry)
					break;
				entry = marker[j];
				marker[j] = marker[j - 1] << 1;
			}
		}

		// A tree with leaves left over is malformed, except a codebook of one entry, which has no tree to speak of
		if (used != 1)
		{
			for (var i = 1; i < 33; i++)
			{
				if ((marker[i] & (0xffffffffu >> (32 - i))) != 0)
					throw new InvalidDataException("A Vorbis codebook's codewords don't fill its Huffman tree: the setup header is damaged.");
			}
		}
		return codes;
	}

	private static float[] BuildVectors(int lookup, int entries, int dimensions, int[] multiplicands, float minimum, float delta, bool sequence)
	{
		var vectors = new float[entries * dimensions];
		for (var entry = 0; entry < entries; entry++)
		{
			var last = 0f;
			var divisor = 1L;
			for (var d = 0; d < dimensions; d++)
			{
				var offset = lookup == 1
					? (int)(entry / divisor % multiplicands.Length)
					: entry * dimensions + d;
				var value = multiplicands[offset] * delta + minimum + last;
				if (sequence)
					last = value;
				vectors[entry * dimensions + d] = value;
				if (lookup == 1)
					divisor = Math.Min(divisor * multiplicands.Length, long.MaxValue / 2);
			}
		}
		return vectors;
	}

	// The largest r with r^dimensions <= entries
	private static long Lookup1Values(int entries, int dimensions)
	{
		var r = (long)Math.Floor(Math.Exp(Math.Log(Math.Max(entries, 1)) / dimensions));
		while (Power(r + 1, dimensions) <= entries)
			r++;
		while (r > 0 && Power(r, dimensions) > entries)
			r--;
		return r;
	}

	private static long Power(long value, int exponent)
	{
		var result = 1L;
		for (var i = 0; i < exponent; i++)
		{
			result *= value;
			if (result > int.MaxValue)
				return long.MaxValue;
		}
		return result;
	}

	// The spec's 32-bit float: a 21-bit mantissa, a sign, and a 10-bit exponent biased by 788
	private static float Float32Unpack(uint x)
	{
		var mantissa = (double)(x & 0x1fffff);
		if ((x & 0x80000000) != 0)
			mantissa = -mantissa;
		var exponent = (int)((x & 0x7fe00000) >> 21);
		return (float)Math.ScaleB(mantissa, exponent - 788);
	}
}
