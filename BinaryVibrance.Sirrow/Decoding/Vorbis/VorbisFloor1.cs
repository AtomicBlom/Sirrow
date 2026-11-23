namespace BinaryVibrance.Sirrow.Decoding.Vorbis;

/// <summary>
/// A type 1 floor: the spectral envelope as a piecewise-linear curve in decibels, its points coded relative to the
/// line between their neighbours. <see cref="Decode"/> reads a channel's points; <see cref="Render"/> draws the curve as
/// linear gains. Type 1 is the floor every encoder since libvorbis 1.0 writes.
/// </summary>
internal sealed class VorbisFloor1
{
	// The spec's floor1_inverse_dB_table: 256 steps from -140 dB to 0 dB, each a factor of 10^(7/256)
	private static readonly float[] InverseDb = MakeInverseDb();
	private static readonly int[] Ranges = [256, 128, 86, 64];

	private readonly int[] _partitionClass;
	private readonly int[] _classDimensions;
	private readonly int[] _classSubclasses;
	private readonly int[] _classMasterbook;
	private readonly int[][] _subclassBooks; // -1: that subclass codes nothing (its points are 0)
	private readonly int _multiplier;
	private readonly int _range;
	private readonly int[] _x;
	private readonly int[] _sorted; // the points' indices in order of x
	private readonly int[] _low;    // each point's neighbours among those before it, by x
	private readonly int[] _high;
	private readonly int[] _finalY;
	private readonly bool[] _step2;

	private VorbisFloor1(int[] partitionClass, int[] classDimensions, int[] classSubclasses, int[] classMasterbook,
		int[][] subclassBooks, int multiplier, int[] x)
	{
		_partitionClass = partitionClass;
		_classDimensions = classDimensions;
		_classSubclasses = classSubclasses;
		_classMasterbook = classMasterbook;
		_subclassBooks = subclassBooks;
		_multiplier = multiplier;
		_range = Ranges[multiplier - 1];
		_x = x;
		_finalY = new int[x.Length];
		_step2 = new bool[x.Length];

		_sorted = new int[x.Length];
		for (var i = 0; i < x.Length; i++)
			_sorted[i] = i;
		Array.Sort(x.ToArray(), _sorted);

		_low = new int[x.Length];
		_high = new int[x.Length];
		for (var i = 2; i < x.Length; i++)
		{
			var low = 0;
			var high = 1;
			for (var j = 0; j < i; j++)
			{
				if (x[j] < x[i] && x[j] > x[low])
					low = j;
				if (x[j] > x[i] && x[j] < x[high])
					high = j;
			}
			_low[i] = low;
			_high[i] = high;
		}
	}

	/// <summary>How many points the floor has: the size of the array <see cref="Decode"/> fills.</summary>
	public int Values => _x.Length;

	/// <summary>Reads a type 1 floor from the setup header.</summary>
	public static VorbisFloor1 Read(VorbisBitReader reader, int codebooks)
	{
		var partitions = (int)reader.ReadBits(5);
		var partitionClass = new int[partitions];
		var maxClass = -1;
		for (var i = 0; i < partitions; i++)
		{
			partitionClass[i] = (int)reader.ReadBits(4);
			maxClass = Math.Max(maxClass, partitionClass[i]);
		}

		var classes = maxClass + 1;
		var dimensions = new int[classes];
		var subclasses = new int[classes];
		var masterbook = new int[classes];
		var subclassBooks = new int[classes][];
		for (var c = 0; c < classes; c++)
		{
			dimensions[c] = (int)reader.ReadBits(3) + 1;
			subclasses[c] = (int)reader.ReadBits(2);
			if (subclasses[c] != 0)
				masterbook[c] = CheckBook((int)reader.ReadBits(8), codebooks);
			subclassBooks[c] = new int[1 << subclasses[c]];
			for (var s = 0; s < subclassBooks[c].Length; s++)
			{
				var book = (int)reader.ReadBits(8) - 1;
				subclassBooks[c][s] = book < 0 ? -1 : CheckBook(book, codebooks);
			}
		}

		var multiplier = (int)reader.ReadBits(2) + 1;
		var rangeBits = (int)reader.ReadBits(4);
		var x = new List<int> { 0, 1 << rangeBits };
		for (var i = 0; i < partitions; i++)
		{
			for (var j = 0; j < dimensions[partitionClass[i]]; j++)
				x.Add((int)reader.ReadBits(rangeBits));
		}
		if (x.Count > 65)
			throw new InvalidDataException($"A Vorbis floor has {x.Count} points; at most 65 are allowed.");
		if (x.Distinct().Count() != x.Count)
			throw new InvalidDataException("A Vorbis floor has two points at the same place: the setup header is damaged.");
		if (reader.EndOfPacket)
			throw new InvalidDataException("The Vorbis setup header ends inside a floor: the file is damaged.");
		return new VorbisFloor1(partitionClass, dimensions, subclasses, masterbook, subclassBooks, multiplier, [.. x]);
	}

	/// <summary>Reads a channel's floor points into <paramref name="y"/>; false when the floor is unused (the channel is silent in this packet).</summary>
	public bool Decode(VorbisBitReader reader, VorbisCodebook[] books, int[] y)
	{
		if (!reader.ReadBit())
			return false;
		var bits = VorbisBitReader.Ilog(_range - 1);
		y[0] = (int)reader.ReadBits(bits);
		y[1] = (int)reader.ReadBits(bits);
		var offset = 2;
		foreach (var c in _partitionClass)
		{
			var dimensions = _classDimensions[c];
			var subclassBits = _classSubclasses[c];
			var mask = (1 << subclassBits) - 1;
			var value = 0;
			if (subclassBits > 0)
			{
				value = books[_classMasterbook[c]].Decode(reader);
				if (value < 0)
					return false;
			}
			for (var j = 0; j < dimensions; j++)
			{
				var book = _subclassBooks[c][value & mask];
				value >>= subclassBits;
				if (book < 0)
				{
					y[offset + j] = 0;
					continue;
				}
				var point = books[book].Decode(reader);
				if (point < 0)
					return false;
				y[offset + j] = point;
			}
			offset += dimensions;
		}
		return !reader.EndOfPacket;
	}

	/// <summary>Draws the curve <see cref="Decode"/> read as linear gains, one a spectral line, across <paramref name="curve"/>.</summary>
	public void Render(int[] y, Span<float> curve)
	{
		// Step 1: each point is coded against the line between its neighbours; recover where it really is, and which points matter
		_finalY[0] = y[0];
		_finalY[1] = y[1];
		_step2[0] = _step2[1] = true;
		for (var i = 2; i < _x.Length; i++)
		{
			var low = _low[i];
			var high = _high[i];
			var predicted = RenderPoint(_x[low], _finalY[low], _x[high], _finalY[high], _x[i]);
			var value = y[i];
			var highRoom = _range - predicted;
			var lowRoom = predicted;
			var room = highRoom < lowRoom ? highRoom * 2 : lowRoom * 2;
			if (value == 0)
			{
				_step2[i] = false;
				_finalY[i] = predicted;
				continue;
			}
			_step2[low] = _step2[high] = _step2[i] = true;
			if (value >= room)
				_finalY[i] = highRoom > lowRoom ? value - lowRoom + predicted : predicted - value + highRoom - 1;
			else
				_finalY[i] = (value & 1) != 0 ? predicted - (value + 1) / 2 : predicted + value / 2;
		}

		// Step 2: lines between the points that matter, in order of x, and flat from the last to the end
		var n = curve.Length;
		var lx = 0;
		var ly = _finalY[_sorted[0]] * _multiplier;
		var hx = 0;
		var hy = 0;
		for (var s = 1; s < _sorted.Length; s++)
		{
			var i = _sorted[s];
			if (!_step2[i])
				continue;
			hy = _finalY[i] * _multiplier;
			hx = _x[i];
			RenderLine(lx, ly, hx, hy, curve);
			lx = hx;
			ly = hy;
		}
		if (hx < n)
			RenderLine(hx, hy, n, hy, curve);
	}

	private static int RenderPoint(int x0, int y0, int x1, int y1, int x)
	{
		var dy = y1 - y0;
		var adx = x1 - x0;
		var offset = Math.Abs(dy) * (x - x0) / adx;
		return dy < 0 ? y0 - offset : y0 + offset;
	}

	// The spec's integer line, from x0 up to but not including x1, as gains; points past the curve's end are dropped
	private static void RenderLine(int x0, int y0, int x1, int y1, Span<float> curve)
	{
		var dy = y1 - y0;
		var adx = x1 - x0;
		var ady = Math.Abs(dy);
		var step = dy / adx;
		var sy = dy < 0 ? step - 1 : step + 1;
		ady -= Math.Abs(step) * adx;
		var y = y0;
		var err = 0;
		if (x0 < curve.Length)
			curve[x0] = InverseDb[Math.Clamp(y, 0, 255)];
		for (var x = x0 + 1; x < x1 && x < curve.Length; x++)
		{
			err += ady;
			if (err >= adx)
			{
				err -= adx;
				y += sy;
			}
			else
				y += step;
			curve[x] = InverseDb[Math.Clamp(y, 0, 255)];
		}
	}

	private static int CheckBook(int book, int codebooks) => book < codebooks
		? book
		: throw new InvalidDataException($"A Vorbis floor names codebook {book} of {codebooks}: the setup header is damaged.");

	private static float[] MakeInverseDb()
	{
		var table = new float[256];
		for (var i = 0; i < 256; i++)
			table[i] = (float)Math.Pow(10.0, 7.0 * (i + 1) / 256.0 - 7.0);
		return table;
	}
}
