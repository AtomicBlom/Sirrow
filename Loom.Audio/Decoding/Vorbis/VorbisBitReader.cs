using System.Numerics;

namespace Loom.Audio.Decoding.Vorbis;

/// <summary>
/// Reads a Vorbis packet's bits, least significant first, as the spec packs them. Reading past the end sets
/// <see cref="EndOfPacket"/> and returns zero rather than throwing: in an audio packet that is a normal condition, which
/// ends the floor or residue being decoded (the spec's "end-of-packet").
/// </summary>
internal sealed class VorbisBitReader
{
	private byte[] _data = [];
	private int _length;
	private int _byte;
	private int _bit;

	public bool EndOfPacket { get; private set; }

	public void Reset(byte[] data, int length)
	{
		_data = data;
		_length = length;
		_byte = 0;
		_bit = 0;
		EndOfPacket = false;
	}

	/// <summary>The next <paramref name="count"/> bits (0 to 32), or 0 with <see cref="EndOfPacket"/> set if the packet has fewer.</summary>
	public uint ReadBits(int count)
	{
		if (count == 0)
			return 0;
		var value = Peek(count, out var available);
		if (available < count)
		{
			SetEnd();
			return 0;
		}
		Skip(count);
		return value;
	}

	public bool ReadBit() => ReadBits(1) != 0;

	/// <summary>The next <paramref name="count"/> bits (up to 32) without consuming them, zero past the end; <paramref name="available"/> says how many are real.</summary>
	public uint Peek(int count, out int available)
	{
		var value = 0u;
		var got = 0;
		var b = _byte;
		var bit = _bit;
		while (got < count && b < _length)
		{
			var take = Math.Min(8 - bit, count - got);
			value |= (uint)((_data[b] >> bit) & ((1 << take) - 1)) << got;
			got += take;
			bit += take;
			if (bit == 8)
			{
				bit = 0;
				b++;
			}
		}
		available = got;
		return value;
	}

	/// <summary>Consumes <paramref name="count"/> bits; past the end, sets <see cref="EndOfPacket"/>.</summary>
	public void Skip(int count)
	{
		var position = (long)_byte * 8 + _bit + count;
		if (position > (long)_length * 8)
		{
			SetEnd();
			return;
		}
		_byte = (int)(position >> 3);
		_bit = (int)(position & 7);
	}

	/// <summary>The bits needed to hold <paramref name="value"/>: the spec's ilog (0 for 0, 1 for 1, 2 for 2 and 3, ...).</summary>
	public static int Ilog(int value) => value <= 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)value);

	private void SetEnd()
	{
		EndOfPacket = true;
		_byte = _length;
		_bit = 0;
	}
}
