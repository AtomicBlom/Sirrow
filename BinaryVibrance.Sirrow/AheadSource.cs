using BinaryVibrance.Sirrow.Mixing;

namespace BinaryVibrance.Sirrow;

/// <summary>
/// Plays another source rendered ahead of the speaker, off the audio thread: for a source that costs a lot to render and
/// can stand to answer its controls a little late, as music can. A sound effect should play directly.
/// </summary>
/// <remarks>
/// The engine's work (its thread, or <see cref="AudioEngine.Update"/> with <see cref="AudioWork.Update"/>) renders the inner
/// source in blocks of 256 frames into a ring, keeping <see cref="LeadSeconds"/> waiting; the audio thread only copies from
/// the ring. The lead is latency: a change to the inner source's controls is heard up to that much later. The inner source
/// must not be playing, nor be played by anything else while wrapped. Its controls keep one reader, the thread rendering
/// it ahead, and the game thread still writes them.
/// </remarks>
public sealed class AheadSource : AudioSource, IDisposable
{
	private const int Block = 256;

	private readonly AudioEngine _engine;
	private readonly object _gate = new(); // between a play on the game thread and the rendering thread; never the audio thread
	private readonly float[] _left = new float[Block];
	private readonly float[] _right = new float[Block];
	private readonly float[] _chunk = new float[Block * 2];
	private readonly float[] _taken = new float[Block * 2];
	private SampleRing? _ring;
	private int _rate;
	private int _leadFrames;
	private long _framesRendered;
	private long _framesHeard;
	private int _underruns;
	private volatile bool _ended;   // the inner source has finished: what is in the ring is all there is
	private volatile bool _starting; // the worker hasn't yet rendered the first lead: silence, not an underrun
	private Exception? _fault;
	private bool _disposed;

	/// <summary>
	/// <paramref name="inner"/>, played <paramref name="leadSeconds"/> behind its rendering (0.1 by default) when this is
	/// played on <paramref name="engine"/>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">A lead of zero or less, or of more than a second.</exception>
	public AheadSource(AudioEngine engine, AudioSource inner, double leadSeconds = 0.1)
	{
		ArgumentNullException.ThrowIfNull(engine);
		ArgumentNullException.ThrowIfNull(inner);
		if (!double.IsFinite(leadSeconds) || leadSeconds <= 0 || leadSeconds > 1)
			throw new ArgumentOutOfRangeException(nameof(leadSeconds), leadSeconds, "A lead is more than 0 and at most a second.");
		_engine = engine;
		Inner = inner;
		LeadSeconds = leadSeconds;
	}

	/// <summary>The source rendered ahead.</summary>
	public AudioSource Inner { get; }

	/// <summary>How far ahead of the speaker it is rendered, in seconds: the latency its controls answer with.</summary>
	public double LeadSeconds { get; }

	/// <summary>Frames of the inner source the audio thread has taken since this last started.</summary>
	public long FramesHeard => Volatile.Read(ref _framesHeard);

	/// <summary>Frames of it rendered so far: ahead of <see cref="FramesHeard"/> by what waits.</summary>
	public long FramesRendered => Volatile.Read(ref _framesRendered);

	/// <summary>Blocks that found too little waiting, and played silence for the rest.</summary>
	public int Underruns => Volatile.Read(ref _underruns);

	/// <inheritdoc/>
	protected override void OnStart(int sampleRate)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (Inner.IsPlaying)
			throw new InvalidOperationException("The source an AheadSource wraps is playing on its own: stop it, and play only the AheadSource.");
		lock (_gate)
		{
			_rate = sampleRate;
			_leadFrames = Math.Max(Block, (int)Math.Ceiling(LeadSeconds * sampleRate));
			var capacity = 2 * _leadFrames + Block;
			if (_ring is null || _ring.FreeFrames + _ring.Frames < capacity)
				_ring = new SampleRing(capacity, 2);
			_ring.Reset();
			_framesRendered = 0;
			_framesHeard = 0;
			_underruns = 0;
			_ended = false;
			_fault = null;
			Inner.Start(sampleRate);
		}
		// Where the work is done in Update (tests, offline renders) the whole lead is rendered here, so it sounds from its
		// first block and a render is the same however it is run. Where a worker does it, the worker renders the lead, and
		// the source is silent until it has, rather than stalling the frame that started it
		_starting = !_engine.WorksOnUpdate;
		if (!_starting)
			Fill();
		_engine.AddAhead(this);
	}

	/// <summary>
	/// Renders the inner source into the ring until the lead waits: the engine's work thread's, or <see cref="AudioEngine.Update"/>'s,
	/// never the audio thread's.
	/// </summary>
	internal void Fill()
	{
		if (_ended || _disposed)
			return;
		lock (_gate)
		{
			var ring = _ring;
			if (ring is null)
				return;
			try
			{
				while (!_ended && ring.Frames < _leadFrames && ring.FreeFrames >= Block)
				{
					Array.Clear(_left);
					Array.Clear(_right);
					var more = Inner.RenderBlock(_left, _right, new AudioRenderInfo(_rate, Block, _framesRendered));
					for (var i = 0; i < Block; i++)
					{
						_chunk[2 * i] = _left[i];
						_chunk[2 * i + 1] = _right[i];
					}
					ring.Write(_chunk);
					Volatile.Write(ref _framesRendered, _framesRendered + Block);
					if (!more)
						_ended = true;
				}
			}
			catch (Exception ex)
			{
				Interlocked.CompareExchange(ref _fault, ex, null);
				_ended = true;
			}
		}
	}

	/// <summary>What rendering the inner source threw, which ended it; the engine throws it again from Update.</summary>
	internal Exception? TakeFault() => Interlocked.Exchange(ref _fault, null);

	/// <inheritdoc/>
	protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
	{
		var ring = _ring;
		if (ring is null || _disposed)
			return false;
		// Ended is read before the ring, so an empty ring after it really is the end
		var ended = _ended;
		if (_starting)
		{
			// Silence until the worker has the whole lead waiting (or the inner source has ended), so it starts as it means
			// to go on rather than stuttering through its first blocks
			if (ring.Frames < _leadFrames && !ended)
				return true;
			_starting = false;
		}
		var frames = Math.Min(info.Frames, Block);
		var taken = ring.Read(_taken.AsSpan(0, frames * 2));
		for (var i = 0; i < taken; i++)
		{
			left[i] = _taken[2 * i];
			right[i] = _taken[2 * i + 1];
		}
		Volatile.Write(ref _framesHeard, _framesHeard + taken);
		if (taken < frames)
		{
			if (ended)
				return taken > 0;
			Volatile.Write(ref _underruns, _underruns + 1);
		}
		return true;
	}

	/// <summary>Stops it if it plays, and takes it out of the engine's work. From the engine's thread.</summary>
	public void Dispose()
	{
		if (_disposed)
			return;
		if (IsPlaying)
			_engine.Stop(this);
		_engine.RemoveAhead(this);
		_disposed = true;
	}
}
