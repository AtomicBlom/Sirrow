namespace BinaryVibrance.Sirrow.Decoding.Flac;

/// <summary>
/// A FLAC decoder, from RFC 9639: reads the metadata when made, then decodes frames on demand into interleaved 32-bit
/// float. Every frame's header and body are checked against their checksums, so a damaged file throws rather than
/// playing noise. Once made it allocates nothing. It decodes native FLAC of 1 to 8 channels, 4 to 32 bits and fixed or
/// variable block sizes; a file that changes its rate or channel count partway is refused, and FLAC inside Ogg isn't
/// read.
/// </summary>
internal sealed class FlacDecoder : IStreamDecoder
{
	private const int MaxLpcOrder = 32;
	private static readonly byte[] Crc8Table = MakeCrc8Table();
	private static readonly ushort[] Crc16Table = MakeCrc16Table();

	private readonly FlacBitReader _reader;
	private readonly long _firstFrame;  // where in the stream the first frame starts
	private readonly int _maxBlockSize;
	private readonly long[][] _samples; // each channel's samples in the frame just decoded
	private readonly long[] _coefficients = new long[MaxLpcOrder];
	private readonly float[] _output;   // the frame just decoded, interleaved
	private int _outputFrames;
	private int _outputRead;
	private bool _ended; // past the audio, at a tag that follows it

	/// <summary>Reads the file's metadata.</summary>
	/// <exception cref="InvalidDataException">The bytes aren't FLAC, or are damaged.</exception>
	public FlacDecoder(Stream stream)
	{
		var r = _reader = new FlacBitReader(stream);
		if (r.AtEnd)
			throw new InvalidDataException("This isn't a FLAC file: it's empty.");
		var magic = r.ReadBits(32);
		if (magic >> 8 == 0x494433) // "ID3": a tag some taggers put before the fLaC, which isn't part of FLAC
		{
			r.ReadBits(8); // the version's minor byte
			var flags = r.ReadBits(8);
			var size = 0L;
			for (var i = 0; i < 4; i++)
				size = size << 7 | (long)r.ReadBits(8) & 0x7F; // ID3 writes its size seven bits to a byte
			r.SkipBytes(size + ((flags & 0x10) != 0 ? 10 : 0)); // the tag, and its footer if it has one
			magic = r.ReadBits(32);
		}
		if (magic != 0x664C6143) // "fLaC"
			throw new InvalidDataException("This isn't a FLAC file: it doesn't start with fLaC.");

		var haveInfo = false;
		var last = false;
		while (!last)
		{
			r.Mark();
			last = r.ReadBits(1) == 1;
			var type = (int)r.ReadBits(7);
			var length = (int)r.ReadBits(24);
			if (type == 0)
			{
				if (haveInfo || length != 34)
					throw new InvalidDataException("The FLAC file's STREAMINFO block is malformed.");
				ReadStreamInfo(out _maxBlockSize);
				haveInfo = true;
			}
			else if (!haveInfo)
				throw new InvalidDataException("The FLAC file's first metadata block isn't its STREAMINFO.");
			else if (type == 127)
				throw new InvalidDataException("The FLAC file has a metadata block of type 127, which is forbidden.");
			else
				r.SkipBytes(length); // tags, seek table, padding, pictures: nothing the decoder needs
		}
		_firstFrame = r.StreamPosition;

		// Room for a frame stored verbatim, which no encoder exceeds, so the reader's buffer never has to grow
		var verbatim = 18 + Channels * (2 + ((long)_maxBlockSize * (BitsPerSample + 1) + 7) / 8) + 2;
		_reader.EnsureCapacity((int)Math.Min(2 * Math.Max(verbatim, MaxFrameSize), Array.MaxLength));
		_samples = new long[Channels][];
		for (var c = 0; c < Channels; c++)
			_samples[c] = new long[_maxBlockSize];
		_output = new float[_maxBlockSize * Channels];
	}

	public int Channels { get; private set; }

	public int SampleRate { get; private set; }

	/// <summary>The bits in each sample, 4 to 32.</summary>
	public int BitsPerSample { get; private set; }

	/// <summary>
	/// The frames in the file as its STREAMINFO says, or 0 when it doesn't. Only a hint: the decoder
	/// plays every frame there is.
	/// </summary>
	public long TotalFrames { get; private set; }

	/// <summary>The MD5 of the samples as encoded, or sixteen zeros when the encoder didn't take it.</summary>
	internal byte[] Md5 { get; } = new byte[16];

	/// <summary>The frame <see cref="DecodeFrame"/> last decoded: its length, its bits per sample, and each channel's samples.</summary>
	internal int FrameLength { get; private set; }

	internal int FrameBits { get; private set; }

	internal long[][] Samples => _samples;

	private long MaxFrameSize { get; set; }

	/// <inheritdoc/>
	public int Read(Span<float> destination)
	{
		var frames = destination.Length / Channels;
		var done = 0;
		while (done < frames)
		{
			if (_outputRead == _outputFrames)
			{
				if (!DecodeFrame())
					break;
				continue;
			}
			var take = Math.Min(frames - done, _outputFrames - _outputRead);
			_output.AsSpan(_outputRead * Channels, take * Channels).CopyTo(destination[(done * Channels)..]);
			_outputRead += take;
			done += take;
		}
		return done;
	}

	/// <inheritdoc/>
	public void Rewind()
	{
		_reader.Seek(_firstFrame);
		_outputFrames = _outputRead = 0;
		_ended = false;
	}

	/// <summary>
	/// Decodes the next frame into <see cref="Samples"/>, and into the float output <see cref="Read"/> takes from. False at
	/// the end of the file.
	/// </summary>
	internal bool DecodeFrame()
	{
		var r = _reader;
		if (_ended || r.AtEnd)
			return false;

		r.Mark();
		var frameStart = r.StreamPosition;
		var sync = r.ReadBits(16);
		if ((sync & 0xFFFE) != 0xFFF8)
		{
			// "TAG": an ID3v1 tag, which some taggers append after the audio
			if (sync == 0x5441 && r.ReadBits(8) == 'G')
			{
				_ended = true;
				return false;
			}
			throw new InvalidDataException($"The FLAC file is damaged: there's no frame where one belongs, at byte {frameStart}.");
		}
		var blockCode = (int)r.ReadBits(4);
		var rateCode = (int)r.ReadBits(4);
		var channelCode = (int)r.ReadBits(4);
		var sizeCode = (int)r.ReadBits(3);
		var reserved = r.ReadBits(1);
		SkipCodedNumber();
		var blockSize = blockCode switch
		{
			0 => -1,
			1 => 192,
			<= 5 => 576 << (blockCode - 2),
			6 => (int)r.ReadBits(8) + 1,
			7 => (int)r.ReadBits(16) + 1,
			_ => 256 << (blockCode - 8),
		};
		var rate = rateCode switch
		{
			0 => SampleRate,
			1 => 88_200,
			2 => 176_400,
			3 => 192_000,
			4 => 8_000,
			5 => 16_000,
			6 => 22_050,
			7 => 24_000,
			8 => 32_000,
			9 => 44_100,
			10 => 48_000,
			11 => 96_000,
			12 => (int)r.ReadBits(8) * 1000,
			13 => (int)r.ReadBits(16),
			14 => (int)r.ReadBits(16) * 10,
			_ => -1,
		};
		var headerCrc = Crc8(r.SinceMark);
		if (r.ReadBits(8) != headerCrc)
			throw new InvalidDataException($"The FLAC file is damaged: the frame at byte {frameStart} fails its header checksum.");
		if (reserved != 0 || blockSize < 0 || rate < 0 || sizeCode == 3 || channelCode > 10)
			throw new InvalidDataException($"The FLAC frame at byte {frameStart} uses a reserved value in its header.");

		var channels = channelCode < 8 ? channelCode + 1 : 2;
		if (channels != Channels)
			throw new NotSupportedException($"The FLAC file changes from {Channels} channels to {channels} partway: it plays one channel layout throughout.");
		if (rate != SampleRate)
			throw new NotSupportedException($"The FLAC file changes from {SampleRate} Hz to {rate} Hz partway: it plays at one rate throughout.");
		if (blockSize > _maxBlockSize)
			throw new InvalidDataException($"The FLAC frame at byte {frameStart} holds {blockSize} samples, more than the file's STREAMINFO says any does ({_maxBlockSize}).");
		var bits = sizeCode switch
		{
			0 => BitsPerSample,
			1 => 8,
			2 => 12,
			4 => 16,
			5 => 20,
			6 => 24,
			_ => 32,
		};

		// Each channel, the side channel of a stereo pair one bit wider than the others
		for (var c = 0; c < channels; c++)
		{
			var side = channelCode == 8 && c == 1 || channelCode == 9 && c == 0 || channelCode == 10 && c == 1;
			DecodeSubframe(_samples[c], blockSize, bits + (side ? 1 : 0));
		}
		r.AlignToByte();
		if (r.ReadBits(16) != Crc16(r.SinceMark[..^2]))
			throw new InvalidDataException($"The FLAC file is damaged: the frame at byte {frameStart} fails its checksum.");

		Decorrelate(channelCode, blockSize);
		FrameLength = blockSize;
		FrameBits = bits;

		var scale = 1f / (1L << (bits - 1));
		for (var c = 0; c < channels; c++)
		{
			var samples = _samples[c];
			for (var i = 0; i < blockSize; i++)
				_output[i * channels + c] = samples[i] * scale;
		}

		_outputFrames = blockSize;
		_outputRead = 0;
		return true;
	}

	private void ReadStreamInfo(out int maxBlockSize)
	{
		var r = _reader;
		var minBlockSize = (int)r.ReadBits(16);
		maxBlockSize = (int)r.ReadBits(16);
		r.ReadBits(24); // the smallest frame
		MaxFrameSize = (long)r.ReadBits(24);
		SampleRate = (int)r.ReadBits(20);
		Channels = (int)r.ReadBits(3) + 1;
		BitsPerSample = (int)r.ReadBits(5) + 1;
		TotalFrames = (long)r.ReadBits(36);
		for (var i = 0; i < Md5.Length; i++)
			Md5[i] = (byte)r.ReadBits(8);
		if (SampleRate == 0)
			throw new InvalidDataException("The FLAC file's sample rate is 0.");
		if (BitsPerSample < 4)
			throw new InvalidDataException($"The FLAC file says it has {BitsPerSample}-bit samples; the least is 4.");
		if (maxBlockSize < 16 || minBlockSize > maxBlockSize)
			throw new InvalidDataException($"The FLAC file's block sizes ({minBlockSize} to {maxBlockSize}) are out of range.");
	}

	// The frame or sample number, in UTF-8's scheme of lengths: nothing the decoder needs, as it reads from the start
	private void SkipCodedNumber()
	{
		var first = (int)_reader.ReadBits(8);
		var more = first switch
		{
			< 0x80 => 0,
			< 0xC0 => -1,
			< 0xE0 => 1,
			< 0xF0 => 2,
			< 0xF8 => 3,
			< 0xFC => 4,
			< 0xFE => 5,
			0xFE => 6,
			_ => -1,
		};
		if (more < 0)
			throw new InvalidDataException("The FLAC file is damaged: a frame's number is malformed.");
		for (var i = 0; i < more; i++)
		{
			if ((_reader.ReadBits(8) & 0xC0) != 0x80)
				throw new InvalidDataException("The FLAC file is damaged: a frame's number is malformed.");
		}
	}

	private void DecodeSubframe(long[] samples, int blockSize, int bits)
	{
		var r = _reader;
		var padding = r.ReadBits(1);
		var type = (int)r.ReadBits(6);
		var wasted = r.ReadBits(1) == 1 ? (int)r.ReadUnary() + 1 : 0;
		if (padding != 0)
			throw new InvalidDataException("The FLAC file is damaged: a subframe header's first bit isn't zero.");
		if (wasted >= bits)
			throw new InvalidDataException($"A FLAC subframe says {wasted} of its {bits} bits are always zero.");
		bits -= wasted; // the low bits every sample has clear, left off and put back after

		var block = samples.AsSpan(0, blockSize);
		switch (type)
		{
			case 0:
				block.Fill(r.ReadSigned(bits));
				break;
			case 1:
				for (var i = 0; i < blockSize; i++)
					samples[i] = r.ReadSigned(bits);
				break;
			case >= 8 and <= 12:
				var fixedOrder = type - 8;
				ReadWarmUp(samples, blockSize, fixedOrder, bits);
				ReadResidual(samples, blockSize, fixedOrder);
				PredictFixed(block, fixedOrder);
				break;
			case >= 32:
				var order = type - 31;
				ReadWarmUp(samples, blockSize, order, bits);
				var precision = (int)r.ReadBits(4) + 1;
				var shift = (int)r.ReadSigned(5);
				if (precision == 16 || shift < 0)
					throw new InvalidDataException("A FLAC subframe's predictor is malformed.");
				for (var i = 0; i < order; i++)
					_coefficients[i] = r.ReadSigned(precision);
				ReadResidual(samples, blockSize, order);
				PredictLinear(block, _coefficients.AsSpan(0, order), shift);
				break;
			default:
				throw new InvalidDataException($"A FLAC subframe has type {type}, which is reserved.");
		}

		if (wasted > 0)
		{
			for (var i = 0; i < blockSize; i++)
				samples[i] <<= wasted;
		}
	}

	private void ReadWarmUp(long[] samples, int blockSize, int order, int bits)
	{
		if (order > blockSize)
			throw new InvalidDataException($"A FLAC subframe's predictor (order {order}) is longer than its {blockSize} samples.");
		for (var i = 0; i < order; i++)
			samples[i] = _reader.ReadSigned(bits);
	}

	// The prediction's errors, Rice-coded in 2^order partitions, each with its own parameter (or stored raw)
	private void ReadResidual(long[] samples, int blockSize, int predictorOrder)
	{
		var r = _reader;
		var method = (int)r.ReadBits(2);
		if (method > 1)
			throw new InvalidDataException($"A FLAC subframe codes its residual with method {method}, which is reserved.");
		var parameterBits = method == 0 ? 4 : 5;
		var escape = (1 << parameterBits) - 1;
		var partitionOrder = (int)r.ReadBits(4);
		var partitionSize = blockSize >> partitionOrder;
		if (partitionSize << partitionOrder != blockSize || partitionSize < predictorOrder)
			throw new InvalidDataException($"A FLAC subframe's residual can't be split into {1 << partitionOrder} partitions of its {blockSize} samples.");

		var i = predictorOrder;
		for (var p = 0; p < 1 << partitionOrder; p++)
		{
			var end = (p + 1) * partitionSize;
			var parameter = (int)r.ReadBits(parameterBits);
			if (parameter == escape)
			{
				var bits = (int)r.ReadBits(5);
				for (; i < end; i++)
					samples[i] = r.ReadSigned(bits);
			}
			else
			{
				for (; i < end; i++)
					samples[i] = r.ReadRice(parameter);
			}
		}
	}

	// The fixed polynomial predictors, orders 0 to 4: each sample is its residual plus the extrapolation of those before
	private static void PredictFixed(Span<long> s, int order)
	{
		switch (order)
		{
			case 1:
				for (var i = 1; i < s.Length; i++)
					s[i] += s[i - 1];
				break;
			case 2:
				for (var i = 2; i < s.Length; i++)
					s[i] += 2 * s[i - 1] - s[i - 2];
				break;
			case 3:
				for (var i = 3; i < s.Length; i++)
					s[i] += 3 * s[i - 1] - 3 * s[i - 2] + s[i - 3];
				break;
			case 4:
				for (var i = 4; i < s.Length; i++)
					s[i] += 4 * s[i - 1] - 6 * s[i - 2] + 4 * s[i - 3] - s[i - 4];
				break;
		}
	}

	private static void PredictLinear(Span<long> s, ReadOnlySpan<long> coefficients, int shift)
	{
		var order = coefficients.Length;
		for (var i = order; i < s.Length; i++)
		{
			var sum = 0L;
			for (var j = 0; j < order; j++)
				sum += coefficients[j] * s[i - 1 - j];
			s[i] += sum >> shift;
		}
	}

	// A stereo pair coded as one channel and the difference (side), or as their average (mid) and difference
	private void Decorrelate(int channelCode, int blockSize)
	{
		var a = _samples[0].AsSpan(0, blockSize);
		var b = _samples[1 % _samples.Length].AsSpan(0, blockSize);
		switch (channelCode)
		{
			case 8: // left, side
				for (var i = 0; i < blockSize; i++)
					b[i] = a[i] - b[i];
				break;
			case 9: // side, right
				for (var i = 0; i < blockSize; i++)
					a[i] += b[i];
				break;
			case 10: // mid, side: the mid lost its lowest bit, which is the side's
				for (var i = 0; i < blockSize; i++)
				{
					var side = b[i];
					var mid = a[i] << 1 | side & 1;
					a[i] = (mid + side) >> 1;
					b[i] = (mid - side) >> 1;
				}
				break;
		}
	}

	private static byte Crc8(ReadOnlySpan<byte> bytes)
	{
		byte crc = 0;
		foreach (var b in bytes)
			crc = Crc8Table[crc ^ b];
		return crc;
	}

	private static ushort Crc16(ReadOnlySpan<byte> bytes)
	{
		ushort crc = 0;
		foreach (var b in bytes)
			crc = (ushort)(crc << 8 ^ Crc16Table[crc >> 8 ^ b]);
		return crc;
	}

	// CRC-8 with polynomial x^8 + x^2 + x + 1, over each frame header
	private static byte[] MakeCrc8Table()
	{
		var table = new byte[256];
		for (var i = 0; i < 256; i++)
		{
			var crc = i;
			for (var bit = 0; bit < 8; bit++)
				crc = (crc & 0x80) != 0 ? crc << 1 ^ 0x07 : crc << 1;
			table[i] = (byte)crc;
		}
		return table;
	}

	// CRC-16 with polynomial x^16 + x^15 + x^2 + 1, over each whole frame
	private static ushort[] MakeCrc16Table()
	{
		var table = new ushort[256];
		for (var i = 0; i < 256; i++)
		{
			var crc = i << 8;
			for (var bit = 0; bit < 8; bit++)
				crc = (crc & 0x8000) != 0 ? crc << 1 ^ 0x8005 : crc << 1;
			table[i] = (ushort)crc;
		}
		return table;
	}
}
