namespace Loom.Audio.Mixing;

/// <summary>
/// The thread that does the engine's work off the audio thread: it keeps every open <see cref="AudioStream"/>'s ring topped
/// up, decoding a little ahead, and renders every <see cref="AheadSource"/> ahead of the speaker. Neither decoding, a file's
/// reads, nor an ahead source's rendering ever happens on the audio thread. It wakes every 2 ms while there are ahead
/// sources (whose lead is short), and every 10 ms otherwise (a stream's ring holds half a second).
/// </summary>
internal sealed class AudioWorker : IDisposable
{
	private readonly Thread _thread;
	private volatile AudioStream[] _streams = [];
	private volatile AheadSource[] _ahead = [];
	private volatile bool _stopping;

	public AudioWorker()
	{
		_thread = new Thread(Run) { IsBackground = true, Name = "Loom audio worker", Priority = ThreadPriority.AboveNormal };
		_thread.Start();
	}

	/// <summary>The streams to keep topped up: replaced whole by the game thread as streams open and close.</summary>
	public void SetStreams(AudioStream[] streams) => _streams = streams;

	/// <summary>The sources to render ahead: replaced whole by the game thread as they start and are disposed.</summary>
	public void SetAhead(AheadSource[] ahead) => _ahead = ahead;

	public void Dispose()
	{
		_stopping = true;
		_thread.Join();
	}

	private void Run()
	{
		while (!_stopping)
		{
			foreach (var stream in _streams)
				stream.Fill(int.MaxValue);
			var ahead = _ahead;
			foreach (var source in ahead)
				source.Fill();
			Thread.Sleep(ahead.Length > 0 ? 2 : 10);
		}
	}
}
