using System.Numerics;

namespace BinaryVibrance.Sirrow.Decoding.Flac;

/// <summary>
/// Reads a FLAC file's bits, most significant first, from a stream it buffers itself. Bits are taken through a 64-bit
/// cache. The bytes from the <see cref="Mark"/> on (the frame being decoded) stay in the buffer, so its checksums can
/// be taken over them once it's read; the buffer only grows if a frame outgrows it.
/// </summary>
internal sealed class FlacBitReader(Stream stream)
{
	private byte[] _buffer = new byte[64 * 1024];
	private long _bufferStart = stream.Position; // where in the stream _buffer[0] came from
	private int _end;                            // the bytes in the buffer
	private int _pos;                            // the next byte to go into the cache
	private ulong _cache;                        // the bits read ahead, left-aligned; the bits below them are zero
	private int _bits;
	private int _mark;
	private bool _streamEnded;

	/// <summary>The byte the next bit is in. Only meaningful on a byte boundary.</summary>
	public int BytePosition => _pos - (_bits >> 3);

	/// <summary>Where in the stream the next byte is. Only meaningful on a byte boundary.</summary>
	public long StreamPosition => _bufferStart + BytePosition;

	/// <summary>The bytes from the mark to the next byte: the frame read so far.</summary>
	public ReadOnlySpan<byte> SinceMark => _buffer.AsSpan(_mark, BytePosition - _mark);

	/// <summary>Whether every byte has been read. Only meaningful on a byte boundary.</summary>
	public bool AtEnd
	{
		get
		{
			if (_bits > 0)
				return false;
			while (_pos == _end)
			{
				if (!Fill())
					return true;
			}
			return false;
		}
	}

	/// <summary>Makes sure the buffer holds at least <paramref name="bytes"/>, so a frame that size never makes it grow.</summary>
	public void EnsureCapacity(int bytes)
	{
		if (bytes > _buffer.Length)
			Array.Resize(ref _buffer, bytes);
	}

	/// <summary>Keeps the bytes from the next one on in the buffer, until the next mark. On a byte boundary.</summary>
	public void Mark() => _mark = BytePosition;

	/// <summary>Skips the bits to the next byte boundary.</summary>
	public void AlignToByte()
	{
		var drop = _bits & 7;
		_cache <<= drop;
		_bits -= drop;
	}

	/// <summary>An unsigned value of 0 to 57 bits.</summary>
	public ulong ReadBits(int count)
	{
		if (count == 0)
			return 0;
		if (_bits < count)
		{
			Load();
			if (_bits < count)
				throw CutShort();
		}
		var value = _cache >> (64 - count);
		_cache <<= count;
		_bits -= count;
		return value;
	}

	/// <summary>A two's-complement value of 0 to 57 bits.</summary>
	public long ReadSigned(int count)
	{
		if (count == 0)
			return 0;
		var shift = 64 - count;
		return (long)(ReadBits(count) << shift) >> shift;
	}

	/// <summary>The number of zero bits before the next one bit, which is read too.</summary>
	public ulong ReadUnary()
	{
		ulong count = 0;
		while (true)
		{
			// The bits below the cached ones are zero, so a set bit anywhere is a real one
			if (_cache != 0)
			{
				var zeros = BitOperations.LeadingZeroCount(_cache);
				_cache <<= zeros;
				_cache <<= 1; // apart from the first shift, since shifting a ulong by 64 shifts it by nothing
				_bits -= zeros + 1;
				return count + (ulong)zeros;
			}
			count += (ulong)_bits;
			_bits = 0;
			Load();
			if (_bits == 0)
				throw CutShort();
		}
	}

	/// <summary>A Rice-coded signed value with parameter <paramref name="parameter"/>: a unary quotient, then the remainder's bits, zigzag-folded.</summary>
	public long ReadRice(int parameter)
	{
		var folded = ReadUnary() << parameter | ReadBits(parameter);
		return (long)(folded >> 1) ^ -(long)(folded & 1);
	}

	/// <summary>Skips <paramref name="bytes"/> bytes, from a byte boundary, without keeping them.</summary>
	public void SkipBytes(long bytes)
	{
		while (bytes > 0 && _bits > 0)
		{
			ReadBits(8);
			bytes--;
		}
		while (bytes > 0)
		{
			if (_pos == _end)
			{
				_mark = _pos;
				if (!Fill())
					throw CutShort();
			}
			var take = (int)Math.Min(bytes, _end - _pos);
			_pos += take;
			bytes -= take;
		}
	}

	/// <summary>Starts reading again from <paramref name="position"/> in the stream, which must be seekable.</summary>
	public void Seek(long position)
	{
		stream.Position = position;
		_bufferStart = position;
		_end = _pos = _mark = _bits = 0;
		_cache = 0;
		_streamEnded = false;
	}

	public static InvalidDataException CutShort() => new("The FLAC file is cut short: it ends partway through a frame.");

	// Tops the cache up to at least 57 bits, or as far as the file goes
	private void Load()
	{
		while (_bits <= 56)
		{
			if (_pos == _end && !Fill())
				return;
			_cache |= (ulong)_buffer[_pos++] << (56 - _bits);
			_bits += 8;
		}
	}

	// Reads more of the stream, after moving the bytes from the mark on down to the start of the buffer. False at its end
	private bool Fill()
	{
		if (_streamEnded)
			return false;
		if (_mark > 0)
		{
			_buffer.AsSpan(_mark, _end - _mark).CopyTo(_buffer);
			_bufferStart += _mark;
			_pos -= _mark;
			_end -= _mark;
			_mark = 0;
		}
		if (_end == _buffer.Length)
			Array.Resize(ref _buffer, _buffer.Length * 2); // a frame bigger than the file said its frames would be
		var read = stream.Read(_buffer, _end, _buffer.Length - _end);
		if (read == 0)
		{
			_streamEnded = true;
			return false;
		}
		_end += read;
		return true;
	}
}
