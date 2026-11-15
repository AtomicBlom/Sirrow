namespace Loom.Audio.Decoding.Vorbis;

/// <summary>
/// An Ogg Vorbis decoder, from the Vorbis I specification: reads the three headers when made, then decodes audio packets
/// on demand into interleaved 32-bit float. Once made it allocates nothing, so a stream can be decoded on any thread
/// without setting off a collection. It decodes every Vorbis I stream an encoder writes today; floor type 0, which no
/// encoder has written since libvorbis 1.0 in 2002, is refused.
/// </summary>
internal sealed class VorbisDecoder : IStreamDecoder
{
	private readonly OggPacketReader _ogg;
	private readonly VorbisBitReader _bits = new();
	private readonly int _blockSize0;
	private readonly int _blockSize1;
	private readonly VorbisCodebook[] _books;
	private readonly VorbisFloor1[] _floors;
	private readonly VorbisResidue[] _residues;
	private readonly VorbisMapping[] _mappings;
	private readonly VorbisMode[] _modes;
	private readonly int _modeBits;
	private readonly Imdct _imdct0;
	private readonly Imdct _imdct1;
	private readonly float[] _slope0; // the rising half of a window, blocksize 0 / 2 long
	private readonly float[] _slope1;

	private readonly float[][] _spectra;   // each channel's spectrum, then its samples' first half
	private readonly float[][] _floorCurves;
	private readonly float[][] _blocks;    // each channel's windowed block
	private readonly float[][] _previous;  // each channel's last block's second half, to overlap
	private readonly bool[] _floorUnused;
	private readonly bool[] _noResidue;
	private readonly int[] _floorY = new int[65];
	private readonly float[] _output;      // the frames the last packet finished, interleaved
	private int _outputFrames;
	private int _outputRead;
	private int _previousSize;             // the last block's size; 0 before the first audio packet
	private long _framesOut;

	/// <summary>Reads the stream's headers.</summary>
	/// <exception cref="InvalidDataException">The stream isn't Ogg Vorbis, or is damaged.</exception>
	/// <exception cref="NotSupportedException">The stream uses floor type 0.</exception>
	public VorbisDecoder(Stream stream)
	{
		_ogg = new OggPacketReader(stream);
		var r = _bits;

		NextHeader(1, "identification");
		if (r.ReadBits(32) != 0)
			throw new InvalidDataException("The Vorbis stream isn't version 0, the only Vorbis I version.");
		Channels = (int)r.ReadBits(8);
		SampleRate = (int)r.ReadBits(32);
		r.ReadBits(32); // the bitrates: maximum, nominal, minimum
		r.ReadBits(32);
		r.ReadBits(32);
		_blockSize0 = 1 << (int)r.ReadBits(4);
		_blockSize1 = 1 << (int)r.ReadBits(4);
		if (!r.ReadBit() || r.EndOfPacket)
			throw new InvalidDataException("The Vorbis identification header is damaged.");
		if (Channels == 0 || SampleRate <= 0)
			throw new InvalidDataException($"The Vorbis stream says it has {Channels} channels at {SampleRate} Hz.");
		if (_blockSize0 < 64 || _blockSize1 > 8192 || _blockSize0 > _blockSize1)
			throw new InvalidDataException($"The Vorbis stream's block sizes ({_blockSize0}, {_blockSize1}) are out of range.");

		NextHeader(3, "comment"); // the tags: nothing the decoder needs

		NextHeader(5, "setup");
		_books = new VorbisCodebook[r.ReadBits(8) + 1];
		for (var i = 0; i < _books.Length; i++)
			_books[i] = VorbisCodebook.Read(r);

		var times = (int)r.ReadBits(6) + 1;
		for (var i = 0; i < times; i++)
		{
			if (r.ReadBits(16) != 0)
				throw new InvalidDataException("The Vorbis setup header has a time-domain transform, which Vorbis I reserves.");
		}

		_floors = new VorbisFloor1[r.ReadBits(6) + 1];
		for (var i = 0; i < _floors.Length; i++)
		{
			_floors[i] = r.ReadBits(16) switch
			{
				0 => throw new NotSupportedException("The Vorbis stream uses floor type 0, which no encoder has written since libvorbis 1.0: re-encode it."),
				1 => VorbisFloor1.Read(r, _books.Length),
				var type => throw new InvalidDataException($"The Vorbis stream has floor type {type}; only 0 and 1 exist."),
			};
		}

		_residues = new VorbisResidue[r.ReadBits(6) + 1];
		for (var i = 0; i < _residues.Length; i++)
		{
			var type = (int)r.ReadBits(16);
			if (type > 2)
				throw new InvalidDataException($"The Vorbis stream has residue type {type}; only 0, 1 and 2 exist.");
			_residues[i] = VorbisResidue.Read(r, type, _books, Channels, _blockSize1 / 2);
		}

		_mappings = new VorbisMapping[r.ReadBits(6) + 1];
		for (var i = 0; i < _mappings.Length; i++)
		{
			if (r.ReadBits(16) != 0)
				throw new InvalidDataException("The Vorbis stream has a mapping type other than 0, the only one.");
			_mappings[i] = VorbisMapping.Read(r, Channels, _floors.Length, _residues.Length);
		}

		_modes = new VorbisMode[r.ReadBits(6) + 1];
		for (var i = 0; i < _modes.Length; i++)
		{
			var longBlock = r.ReadBit();
			var window = r.ReadBits(16);
			var transform = r.ReadBits(16);
			var mapping = (int)r.ReadBits(8);
			if (window != 0 || transform != 0 || mapping >= _mappings.Length)
				throw new InvalidDataException("A Vorbis mode is malformed: the setup header is damaged.");
			_modes[i] = new VorbisMode(longBlock, mapping);
		}
		if (!r.ReadBit() || r.EndOfPacket)
			throw new InvalidDataException("The Vorbis setup header is damaged: it doesn't end with its framing bit.");
		_modeBits = VorbisBitReader.Ilog(_modes.Length - 1);

		_imdct0 = new Imdct(_blockSize0);
		_imdct1 = new Imdct(_blockSize1);
		_slope0 = Slope(_blockSize0 / 2);
		_slope1 = Slope(_blockSize1 / 2);
		_spectra = Jagged(Channels, _blockSize1 / 2);
		_floorCurves = Jagged(Channels, _blockSize1 / 2);
		_blocks = Jagged(Channels, _blockSize1);
		_previous = Jagged(Channels, _blockSize1 / 2);
		_floorUnused = new bool[Channels];
		_noResidue = new bool[Channels];
		_output = new float[_blockSize1 / 2 * Channels];
	}

	public int Channels { get; }

	public int SampleRate { get; }

	/// <summary>
	/// Decodes up to <paramref name="destination"/>'s length in interleaved samples; returns the frames written, fewer only
	/// at the end of the stream.
	/// </summary>
	public int Read(Span<float> destination)
	{
		var frames = destination.Length / Channels;
		var done = 0;
		while (done < frames)
		{
			if (_outputRead == _outputFrames)
			{
				if (!DecodeNextPacket())
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

	/// <summary>Goes back to the stream's first sample. The stream must be seekable.</summary>
	public void Rewind()
	{
		_ogg.Rewind();
		for (var i = 0; i < 3; i++)
		{
			if (!_ogg.TryRead())
				throw new InvalidDataException("The Ogg stream lost its headers.");
		}
		_previousSize = 0;
		_outputFrames = _outputRead = 0;
		_framesOut = 0;
	}

	private void NextHeader(int type, string name)
	{
		if (!_ogg.TryRead())
			throw new InvalidDataException($"The Ogg stream ends before the Vorbis {name} header.");
		_bits.Reset(_ogg.Packet, _ogg.PacketLength);
		var ok = _bits.ReadBits(8) == type;
		foreach (var c in "vorbis")
			ok &= _bits.ReadBits(8) == c;
		if (!ok)
			throw new InvalidDataException($"This isn't an Ogg Vorbis stream: its packet where the {name} header belongs is something else.");
	}

	private bool DecodeNextPacket()
	{
		while (_ogg.TryRead())
		{
			if (_ogg.PacketLength == 0)
				continue;
			_bits.Reset(_ogg.Packet, _ogg.PacketLength);
			if (_bits.ReadBit())
				continue; // a header packet where audio belongs: ignored, as the spec says
			var frames = DecodeAudio();
			if (frames < 0)
				continue;

			// The last page's granule position is the stream's length: the last packet's padding past it is dropped
			if (_ogg.IsLast && _ogg.Granule >= 0)
				frames = (int)Math.Clamp(_ogg.Granule - _framesOut, 0, frames);
			_framesOut += frames;
			_outputFrames = frames;
			_outputRead = 0;
			return true;
		}
		return false;
	}

	// One audio packet, after its type bit: decodes a block, overlaps it with the last, and leaves the finished frames in
	// _output. Returns how many, or -1 for a packet too short to decode, which is skipped
	private int DecodeAudio()
	{
		var r = _bits;
		var modeNumber = (int)r.ReadBits(_modeBits);
		if (r.EndOfPacket || modeNumber >= _modes.Length)
			return -1;
		var mode = _modes[modeNumber];
		var n = mode.LongBlock ? _blockSize1 : _blockSize0;
		var half = n / 2;
		var previousLong = false;
		var nextLong = false;
		if (mode.LongBlock)
		{
			previousLong = r.ReadBit();
			nextLong = r.ReadBit();
			if (r.EndOfPacket)
				return -1;
		}
		var mapping = _mappings[mode.Mapping];

		// Floors: each channel's envelope, or none (the channel is silent this packet)
		for (var c = 0; c < Channels; c++)
		{
			_spectra[c].AsSpan(0, half).Clear();
			var floor = _floors[mapping.SubmapFloor[mapping.Mux[c]]];
			_floorUnused[c] = !floor.Decode(r, _books, _floorY);
			if (!_floorUnused[c])
				floor.Render(_floorY, _floorCurves[c].AsSpan(0, half));
			_noResidue[c] = _floorUnused[c];
		}

		// A coupled pair's residue is decoded if either of the two has a floor
		for (var i = 0; i < mapping.Magnitude.Length; i++)
		{
			if (!_noResidue[mapping.Magnitude[i]] || !_noResidue[mapping.Angle[i]])
				_noResidue[mapping.Magnitude[i]] = _noResidue[mapping.Angle[i]] = false;
		}

		// Residues, a submap at a time
		for (var s = 0; s < mapping.SubmapResidue.Length; s++)
		{
			var channels = mapping.SubmapChannels[s];
			var skip = mapping.SubmapSkip[s];
			for (var j = 0; j < channels.Length; j++)
				skip[j] = _noResidue[channels[j]];
			_residues[mapping.SubmapResidue[s]].Decode(r, _books, _spectra, channels, skip, half);
		}

		// Uncouple, last step first
		for (var i = mapping.Magnitude.Length - 1; i >= 0; i--)
		{
			var magnitude = _spectra[mapping.Magnitude[i]];
			var angle = _spectra[mapping.Angle[i]];
			for (var j = 0; j < half; j++)
			{
				var m = magnitude[j];
				var a = angle[j];
				if (m > 0)
				{
					if (a > 0)
						angle[j] = m - a;
					else
					{
						angle[j] = m;
						magnitude[j] = m + a;
					}
				}
				else if (a > 0)
					angle[j] = m + a;
				else
				{
					angle[j] = m;
					magnitude[j] = m - a;
				}
			}
		}

		// Floor times residue, then back to samples, windowed
		var imdct = mode.LongBlock ? _imdct1 : _imdct0;
		for (var c = 0; c < Channels; c++)
		{
			var spectrum = _spectra[c].AsSpan(0, half);
			if (_floorUnused[c])
				spectrum.Clear();
			else
			{
				var curve = _floorCurves[c];
				for (var j = 0; j < half; j++)
					spectrum[j] *= curve[j];
			}
			var block = _blocks[c].AsSpan(0, n);
			imdct.Inverse(spectrum, block);
			Window(block, mode.LongBlock, previousLong, nextLong);
		}

		// Overlap: from the last block's centre to this one's. The first audio packet only primes the overlap
		var frames = 0;
		if (_previousSize > 0)
		{
			var pn = _previousSize;
			frames = pn / 4 + n / 4;
			var shift = n / 4 - pn / 4;
			for (var c = 0; c < Channels; c++)
			{
				var previous = _previous[c];
				var block = _blocks[c];
				for (var t = 0; t < frames; t++)
				{
					var a = t < pn / 2 ? previous[t] : 0f;
					var b = t + shift >= 0 ? block[t + shift] : 0f;
					_output[t * Channels + c] = a + b;
				}
			}
		}
		for (var c = 0; c < Channels; c++)
			_blocks[c].AsSpan(half, half).CopyTo(_previous[c]);
		_previousSize = n;
		return frames;
	}

	// The spec's window: power-of-sine slopes, each as long as the overlap with the neighbouring block (a short one's
	// where a long block meets a short), flat between, zero outside
	private void Window(Span<float> block, bool longBlock, bool previousLong, bool nextLong)
	{
		var n = block.Length;
		int leftStart, leftSize, rightStart, rightSize;
		if (longBlock && !previousLong)
		{
			leftStart = n / 4 - _blockSize0 / 4;
			leftSize = _blockSize0 / 2;
		}
		else
		{
			leftStart = 0;
			leftSize = n / 2;
		}
		if (longBlock && !nextLong)
		{
			rightStart = n * 3 / 4 - _blockSize0 / 4;
			rightSize = _blockSize0 / 2;
		}
		else
		{
			rightStart = n / 2;
			rightSize = n / 2;
		}

		var left = leftSize == _blockSize0 / 2 ? _slope0 : _slope1;
		var right = rightSize == _blockSize0 / 2 ? _slope0 : _slope1;
		block[..leftStart].Clear();
		for (var i = 0; i < leftSize; i++)
			block[leftStart + i] *= left[i];
		for (var i = 0; i < rightSize; i++)
			block[rightStart + i] *= right[rightSize - 1 - i];
		block[(rightStart + rightSize)..].Clear();
	}

	private static float[] Slope(int size)
	{
		var slope = new float[size];
		for (var i = 0; i < size; i++)
		{
			var s = Math.Sin((i + 0.5) / size * Math.PI / 2);
			slope[i] = (float)Math.Sin(Math.PI / 2 * s * s);
		}
		return slope;
	}

	private static float[][] Jagged(int count, int length)
	{
		var arrays = new float[count][];
		for (var i = 0; i < count; i++)
			arrays[i] = new float[length];
		return arrays;
	}
}
