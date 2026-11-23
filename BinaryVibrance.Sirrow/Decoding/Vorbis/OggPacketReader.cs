using System.Buffers.Binary;

namespace BinaryVibrance.Sirrow.Decoding.Vorbis;

/// <summary>
/// The packets of the first logical stream in an Ogg file, in order: pages are read and checked against their CRC,
/// packets are put back together across pages, and pages of any other stream multiplexed in are skipped. The stream ends
/// at its end-of-stream page; a chained stream after it is not read. Reading allocates nothing once the packet buffer
/// has grown to the largest packet.
/// </summary>
internal sealed class OggPacketReader
{
	private const int HeaderSize = 27;
	private const int MaxPageSize = HeaderSize + 255 + 255 * 255;
	private static readonly uint[] CrcTable = MakeCrcTable();

	private readonly Stream _stream;
	private readonly byte[] _page = new byte[MaxPageSize];
	private byte[] _packet = new byte[16 * 1024];
	private bool _pageLoaded;
	private int _segmentCount;
	private int _segment;
	private int _dataOffset;
	private long _pageGranule;
	private bool _pageEndsStream;
	private bool _serialKnown;
	private int _serial;

	public OggPacketReader(Stream stream) => _stream = stream;

	/// <summary>The last packet read: <see cref="PacketLength"/> bytes of it.</summary>
	public byte[] Packet => _packet;

	public int PacketLength { get; private set; }

	/// <summary>The granule position of the page the last packet ended on, if it was the last packet to end there; else -1.</summary>
	public long Granule { get; private set; }

	/// <summary>Whether the last packet is the stream's last.</summary>
	public bool IsLast { get; private set; }

	/// <summary>Reads the next packet; false at the end of the stream.</summary>
	/// <exception cref="InvalidDataException">The bytes aren't an Ogg stream, or a page is damaged.</exception>
	public bool TryRead()
	{
		var length = 0;
		while (true)
		{
			if (!_pageLoaded || _segment == _segmentCount)
			{
				if (!LoadPage(continuing: length > 0))
				{
					if (length > 0)
						throw new InvalidDataException("The Ogg stream ends inside a packet: the file is cut short.");
					return false;
				}
				continue;
			}

			int size = _page[HeaderSize + _segment];
			if (length + size > _packet.Length)
				Array.Resize(ref _packet, Math.Max(_packet.Length * 2, length + size));
			Buffer.BlockCopy(_page, _dataOffset, _packet, length, size);
			length += size;
			_dataOffset += size;
			_segment++;
			if (size == 255)
				continue;

			// A page's granule is that of the last packet to end on it: this one, unless another ends after it
			var lastToEnd = true;
			for (var s = _segment; s < _segmentCount; s++)
			{
				if (_page[HeaderSize + s] < 255)
				{
					lastToEnd = false;
					break;
				}
			}
			PacketLength = length;
			Granule = lastToEnd ? _pageGranule : -1;
			IsLast = lastToEnd && _pageEndsStream;
			return true;
		}
	}

	/// <summary>Goes back to the start of the stream, which must be seekable.</summary>
	public void Rewind()
	{
		_stream.Position = 0;
		_pageLoaded = false;
		_pageEndsStream = false;
		_serialKnown = false;
		_segment = _segmentCount = 0;
	}

	private bool LoadPage(bool continuing)
	{
		if (_pageLoaded && _pageEndsStream)
			return false;
		while (true)
		{
			// The end of the file between pages is the end of the stream; inside one, the file is cut short
			var first = _stream.Read(_page, 0, HeaderSize);
			if (first == 0)
				return false;
			if (!ReadFully(_page, first, HeaderSize - first))
				throw CutShort();
			if (_page[0] != (byte)'O' || _page[1] != (byte)'g' || _page[2] != (byte)'g' || _page[3] != (byte)'S')
				throw new InvalidDataException("This isn't an Ogg stream: a page doesn't start with 'OggS'.");
			if (_page[4] != 0)
				throw new InvalidDataException($"The Ogg page is version {_page[4]}; only version 0 exists.");
			var headerType = _page[5];
			int segments = _page[26];
			if (!ReadFully(_page, HeaderSize, segments))
				throw CutShort();
			var dataLength = 0;
			for (var s = 0; s < segments; s++)
				dataLength += _page[HeaderSize + s];
			if (!ReadFully(_page, HeaderSize + segments, dataLength))
				throw CutShort();

			var pageLength = HeaderSize + segments + dataLength;
			if (Crc(_page.AsSpan(0, pageLength)) != BinaryPrimitives.ReadUInt32LittleEndian(_page.AsSpan(22)))
				throw new InvalidDataException("An Ogg page fails its checksum: the file is damaged.");

			var serial = BinaryPrimitives.ReadInt32LittleEndian(_page.AsSpan(14));
			if (!_serialKnown)
			{
				_serial = serial;
				_serialKnown = true;
			}
			else if (serial != _serial)
				continue; // another logical stream multiplexed into the file

			_pageGranule = BinaryPrimitives.ReadInt64LittleEndian(_page.AsSpan(6));
			_pageEndsStream = (headerType & 4) != 0;
			_segmentCount = segments;
			_segment = 0;
			_dataOffset = HeaderSize + segments;
			_pageLoaded = true;

			var continued = (headerType & 1) != 0;
			if (continuing && !continued)
				throw new InvalidDataException("An Ogg packet breaks off at a page that doesn't continue it: the file is damaged.");
			if (!continuing && continued)
			{
				// The end of a packet whose start isn't here: skip it
				while (_segment < _segmentCount)
				{
					int size = _page[HeaderSize + _segment];
					_dataOffset += size;
					_segment++;
					if (size < 255)
						break;
				}
			}
			return true;
		}
	}

	private static InvalidDataException CutShort() => new("The Ogg stream ends inside a page: the file is cut short.");

	private bool ReadFully(byte[] buffer, int offset, int count)
	{
		while (count > 0)
		{
			var read = _stream.Read(buffer, offset, count);
			if (read == 0)
				return false;
			offset += read;
			count -= read;
		}
		return true;
	}

	// Ogg's CRC-32: polynomial 0x04c11db7, no reflection, starting at zero, over the page with its CRC field zeroed
	private static uint Crc(ReadOnlySpan<byte> page)
	{
		var crc = 0u;
		for (var i = 0; i < page.Length; i++)
		{
			var b = i is >= 22 and < 26 ? (byte)0 : page[i];
			crc = (crc << 8) ^ CrcTable[(int)((crc >> 24) ^ b)];
		}
		return crc;
	}

	private static uint[] MakeCrcTable()
	{
		var table = new uint[256];
		for (var i = 0u; i < 256; i++)
		{
			var r = i << 24;
			for (var bit = 0; bit < 8; bit++)
				r = (r & 0x80000000) != 0 ? (r << 1) ^ 0x04c11db7 : r << 1;
			table[i] = r;
		}
		return table;
	}
}
