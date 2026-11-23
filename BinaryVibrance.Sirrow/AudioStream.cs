using BinaryVibrance.Sirrow.Decoding;
using BinaryVibrance.Sirrow.Mixing;

namespace BinaryVibrance.Sirrow;

/// <summary>
/// Music decoded as it plays, rather than held decoded in memory as a clip is: an Ogg Vorbis or FLAC file, opened with
/// <see cref="AudioEngine.OpenStream(byte[], bool)"/> and played like any source, with
/// <see cref="AudioEngine.Play(AudioSource, AudioBus, float, float)"/>. Each play starts from the beginning. A looping stream
/// plays until stopped, its end joined to its start without a gap.
/// </summary>
/// <remarks>
/// A thread of the engine's decodes ahead into a ring of half a second (in the browser, the frame thread does, from
/// <see cref="AudioEngine.Update"/>), and the audio thread reads the ring, converting the file's rate to the engine's as
/// it goes. Neither decoding nor reading the file ever happens on the audio thread, and nothing allocates once it plays.
/// </remarks>
public sealed class AudioStream : AudioSource, IDisposable
{
	private const double BufferSeconds = 0.5;
	private const double StartSeconds = 0.1;
	private const int WindowFrames = 4096;

	private readonly AudioEngine _engine;
	private readonly Stream _file;
	private readonly IStreamDecoder _decoder;
	private readonly SampleRing _ring;
	private readonly float[] _chunk;
	private readonly object _gate = new(); // between a play on the game thread and the decoding thread; never the audio thread
	private bool _decoded;                // the decoder has read past its start, so a play must rewind it
	private volatile bool _ended;         // every sample is in the ring: the decoder is at its end, and the stream doesn't loop
	private Exception? _fault;

	// The audio thread's: frames taken from the ring, the read point among them, and where the stream's last frame is
	private readonly float[] _window;
	private int _windowFrames;
	private double _position;
	private double _step;
	private int _endFrame;
	private volatile bool _disposed;

	internal AudioStream(AudioEngine engine, Stream file, IStreamDecoder decoder, bool loop)
	{
		_engine = engine;
		_file = file;
		_decoder = decoder;
		Loop = loop;
		Channels = decoder.Channels;
		SampleRate = decoder.SampleRate;
		_ring = new SampleRing((int)(SampleRate * BufferSeconds), Channels);
		_chunk = new float[1024 * Channels];
		_window = new float[(WindowFrames + 4) * Channels];
	}

	/// <summary>Whether the stream starts over at its end, playing until stopped.</summary>
	public bool Loop { get; }

	/// <summary>1 or 2.</summary>
	public int Channels { get; }

	/// <summary>The file's rate, in frames per second; the engine converts it to its own as it plays.</summary>
	public int SampleRate { get; }

	/// <summary>The first exception decoding threw (a damaged file), which ended the stream; the engine throws it again from Update.</summary>
	internal Exception? TakeFault() => Interlocked.Exchange(ref _fault, null);

	/// <summary>
	/// Stops the stream if it is playing (at once: fade it out with <see cref="AudioEngine.Stop(AudioSource, float)"/> first
	/// for a gentler end), stops decoding it and closes its file. From the engine's thread, as every engine call is.
	/// </summary>
	public void Dispose()
	{
		if (_disposed)
			return;
		_engine.Close(this);
		Close();
	}

	// The engine's half of disposing: no more decoding or playing, and the file closed
	internal void Close()
	{
		_disposed = true;
		lock (_gate)
			_file.Dispose();
	}

	/// <inheritdoc/>
	protected override void OnStart(int sampleRate)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		lock (_gate)
		{
			if (_decoded)
				_decoder.Rewind();
			_ring.Reset();
			_ended = false;
		}
		_step = (double)SampleRate / sampleRate;
		_window.AsSpan(0, Channels).Clear(); // one frame of silence before the first, for the curve to start from
		_windowFrames = 1;
		_position = 1;
		_endFrame = -1;
		Fill((int)(SampleRate * StartSeconds));
	}

	/// <summary>
	/// Decodes into the ring, up to <paramref name="maxFrames"/> or until it's full: the decoding thread's (or the frame
	/// thread's) work, never the audio thread's.
	/// </summary>
	internal void Fill(int maxFrames)
	{
		if (_ended || _disposed)
			return;
		lock (_gate)
		{
			try
			{
				var filled = 0;
				var emptyPasses = 0;
				while (!_ended && filled < maxFrames && _ring.FreeFrames >= _chunk.Length / Channels)
				{
					var frames = _decoder.Read(_chunk);
					_decoded = true;
					if (frames == 0)
					{
						// The end: start over if looping (a file with no samples at all ends instead), else that's all
						if (Loop && emptyPasses++ == 0)
						{
							_decoder.Rewind();
							continue;
						}
						_ended = true;
						break;
					}
					emptyPasses = 0;
					_ring.Write(_chunk.AsSpan(0, frames * Channels));
					filled += frames;
				}
			}
			catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
			{
				Interlocked.CompareExchange(ref _fault, ex, null);
				_ended = true;
			}
		}
	}

	/// <inheritdoc/>
	protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
	{
		if (_disposed)
			return false;
		var channels = Channels;
		for (var i = 0; i < info.Frames; i++)
		{
			var index = (int)_position;
			while (index + 2 >= _windowFrames && (_endFrame < 0 || index < _endFrame) && TakeFromRing())
				index = (int)_position;
			if (_endFrame >= 0 && index >= _endFrame)
				return false; // past the last frame
			if (index + 2 >= _windowFrames)
				return true; // the ring has run dry: silence until the decoder catches up, the music held where it was

			var t = (float)(_position - index);
			var at = index * channels;
			left[i] = Interpolation.Hermite(_window[at - channels], _window[at], _window[at + channels], _window[at + 2 * channels], t);
			if (channels == 2)
				right[i] = Interpolation.Hermite(_window[at - 1], _window[at + 1], _window[at + 3], _window[at + 5], t);
			else
				right[i] = left[i];
			_position += _step;
		}
		return true;
	}

	// Slides the window down to the frame before the read point and fills it from the ring. At the end of the stream,
	// pads it with silence so the curve reaches the last frame. False when there was nothing to take
	private bool TakeFromRing()
	{
		var channels = Channels;
		var keep = Math.Max(0, (int)_position - 1);
		if (keep > 0)
		{
			_window.AsSpan(keep * channels, (_windowFrames - keep) * channels).CopyTo(_window);
			_windowFrames -= keep;
			_position -= keep;
			if (_endFrame >= 0)
				_endFrame -= keep;
		}

		// Ended is read before the ring, so an empty ring after it really is the end
		var ended = _ended;
		var taken = _ring.Read(_window.AsSpan(_windowFrames * channels, (WindowFrames - _windowFrames) * channels));
		_windowFrames += taken;
		if (taken > 0)
			return true;
		if (!ended || _endFrame >= 0)
			return false;
		_endFrame = _windowFrames;
		_window.AsSpan(_windowFrames * channels, 3 * channels).Clear();
		_windowFrames += 3;
		return true;
	}
}
