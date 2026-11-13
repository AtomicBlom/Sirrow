using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Loom.Audio;
using Loom.Core.Diagnostics;

namespace Loom.Hosting.Android.Audio;

/// <summary>
/// Android's audio output, through AAudio: a low-latency output stream in 32-bit float stereo, asking for exclusive
/// use of the output (the MMAP path, where the device has one; AAudio shares it otherwise), with its buffer at two
/// bursts — the floor AAudio's own guidance gives for glitch-free low latency. AAudio calls back on its own
/// high-priority thread with the device's buffer, and the mixer renders straight into it. When the output goes
/// (headphones unplugged, a Bluetooth device dropping), the stream is reopened on the new default output at the same
/// rate, from a thread of its own: AAudio forbids reopening from its callbacks.
/// </summary>
internal sealed unsafe class AAudioDevice : IAudioDevice
{
	// Large enough for any callback a stream makes: AAudio never asks for more than its buffer's capacity
	private const int ScratchFrames = 8192;

	private readonly AudioOptions _options;
	private readonly object _gate = new(); // the game thread's and the reopening thread's; never taken on the audio thread
	private GCHandle _self;
	private nint _stream;
	private int _channels;
	private float[] _scratch = [];
	private IAudioRenderer? _renderer;
	private bool _running;
	private bool _disposed;

	private AAudioDevice(AudioOptions options) => _options = options;

	public string Backend => "AAudio";

	public string Name { get; private set; } = "";

	public int SampleRate { get; private set; }

	public int BufferFrames { get; private set; }

	/// <summary>Opens the output for <paramref name="options"/>, or returns null with the reason.</summary>
	public static AAudioDevice? TryOpen(AudioOptions options, out string reason)
	{
		ArgumentNullException.ThrowIfNull(options);
		var device = new AAudioDevice(options);
		device._self = GCHandle.Alloc(device);
		try
		{
			reason = device.Open(options.SampleRate);
			if (reason.Length == 0)
				return device;
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			reason = $"AAudio would not load: {ex.Message}";
		}
		device.Dispose();
		return null;
	}

	// Opens the stream at `sampleRate` (0: the device's own); "" on success, else why not
	private string Open(int sampleRate)
	{
		nint builder;
		var result = AAudio.AAudio_createStreamBuilder(&builder);
		if (result != AAudio.Ok)
			return $"AAudio would not make a stream builder: {AAudio.Text(result)}";
		try
		{
			AAudio.AAudioStreamBuilder_setDirection(builder, AAudio.DirectionOutput);
			AAudio.AAudioStreamBuilder_setFormat(builder, AAudio.FormatPcmFloat);
			AAudio.AAudioStreamBuilder_setChannelCount(builder, 2);
			AAudio.AAudioStreamBuilder_setSampleRate(builder, sampleRate);
			AAudio.AAudioStreamBuilder_setPerformanceMode(builder, AAudio.PerformanceModeLowLatency);
			AAudio.AAudioStreamBuilder_setSharingMode(builder, AAudio.SharingModeExclusive);
			AAudio.AAudioStreamBuilder_setUsage(builder, AAudio.UsageGame);
			var self = GCHandle.ToIntPtr(_self);
			AAudio.AAudioStreamBuilder_setDataCallback(builder, &OnData, self);
			AAudio.AAudioStreamBuilder_setErrorCallback(builder, &OnError, self);
			nint stream;
			result = AAudio.AAudioStreamBuilder_openStream(builder, &stream);
			if (result != AAudio.Ok)
				return $"AAudio would not open an output stream: {AAudio.Text(result)}";
			_stream = stream;
		}
		finally
		{
			AAudio.AAudioStreamBuilder_delete(builder);
		}

		if (AAudio.AAudioStream_getFormat(_stream) != AAudio.FormatPcmFloat)
			return "AAudio opened the output in a sample format other than float";
		_channels = AAudio.AAudioStream_getChannelCount(_stream);
		if (_channels != 2 && _scratch.Length == 0)
			_scratch = new float[ScratchFrames * 2];

		var burst = AAudio.AAudioStream_getFramesPerBurst(_stream);
		var asked = _options.BufferFrames > 0 ? _options.BufferFrames : burst * 2;
		var granted = AAudio.AAudioStream_setBufferSizeInFrames(_stream, asked); // the size it settled on, or an error
		BufferFrames = granted > 0 ? granted : burst;
		SampleRate = AAudio.AAudioStream_getSampleRate(_stream);
		var exclusive = AAudio.AAudioStream_getSharingMode(_stream) == AAudio.SharingModeExclusive;
		var lowLatency = AAudio.AAudioStream_getPerformanceMode(_stream) == AAudio.PerformanceModeLowLatency;
		Name = $"{(exclusive ? "exclusive" : "shared")} {(lowLatency ? "low-latency" : "standard")} output, {_channels} channels, {burst}-frame bursts";
		return "";
	}

	public void Start(IAudioRenderer renderer)
	{
		ArgumentNullException.ThrowIfNull(renderer);
		if (_renderer is not null)
			throw new InvalidOperationException("The device has already been started.");
		Volatile.Write(ref _renderer, renderer);
		Resume();
	}

	public void Pause()
	{
		lock (_gate)
		{
			_running = false;
			if (_stream != 0)
				AAudio.AAudioStream_requestPause(_stream);
		}
	}

	public void Resume()
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			_running = true;
			if (_stream == 0)
				return;
			var result = AAudio.AAudioStream_requestStart(_stream);
			if (result != AAudio.Ok)
				throw new InvalidOperationException($"AAudio would not start the output: {AAudio.Text(result)}");
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
				return;
			_disposed = true;
			Close();
			if (_self.IsAllocated)
				_self.Free();
		}
	}

	private void Close()
	{
		if (_stream == 0)
			return;
		AAudio.AAudioStream_requestStop(_stream);
		AAudio.AAudioStream_close(_stream);
		_stream = 0;
	}

	// The output went: a new stream on whatever the output is now, at the engine's rate, playing if this one was. Only for
	// the stream that failed: a second error from it, or one arriving after a reopen, finds a stream it doesn't own
	private void Reopen(nint failed)
	{
		lock (_gate)
		{
			if (_disposed || _stream != failed)
				return;
			Close();
			var rate = SampleRate;
			var failure = Open(rate);
			if (failure.Length == 0 && SampleRate != rate)
				failure = $"the new output runs at {SampleRate} Hz, not the engine's {rate} Hz";
			if (failure.Length != 0)
			{
				// No output to go to, or not at the rate the engine's clips were made at: silent rather than wrong
				Close();
				SampleRate = rate;
				LoomLog.Warning($"The audio output went and could not be reopened, so the app is silent: {failure}");
				return;
			}
			LoomLog.Info($"audio output changed; now {Name}");
			if (_running)
				AAudio.AAudioStream_requestStart(_stream);
		}
	}

	// AAudio's audio thread: fill `frames` frames of the stream's buffer
	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static int OnData(nint stream, nint self, void* audioData, int frames)
	{
		var device = (AAudioDevice)GCHandle.FromIntPtr(self).Target!;
		device.Fill(audioData, frames);
		return AAudio.CallbackResultContinue;
	}

	private void Fill(void* audioData, int frames)
	{
		var renderer = Volatile.Read(ref _renderer);
		if (_channels == 2)
		{
			var output = new Span<float>(audioData, frames * 2);
			if (renderer is null)
				output.Clear();
			else
				renderer.Render(output);
			return;
		}

		// Another channel count: render stereo in pieces and lay each frame out as the stream wants it
		var destination = new Span<float>(audioData, frames * _channels);
		for (var offset = 0; offset < frames; offset += ScratchFrames)
		{
			var n = Math.Min(ScratchFrames, frames - offset);
			var stereo = _scratch.AsSpan(0, n * 2);
			if (renderer is null)
				stereo.Clear();
			else
				renderer.Render(stereo);
			for (var f = 0; f < n; f++)
			{
				var frame = destination.Slice((offset + f) * _channels, _channels);
				if (_channels == 1)
				{
					frame[0] = 0.5f * (stereo[f * 2] + stereo[f * 2 + 1]);
					continue;
				}
				frame.Clear();
				frame[0] = stereo[f * 2];
				frame[1] = stereo[f * 2 + 1];
			}
		}
	}

	// AAudio's error thread: the stream has failed. Reopening must wait until this returns, so it goes to a thread of its own
	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static void OnError(nint stream, nint self, int error)
	{
		var device = (AAudioDevice)GCHandle.FromIntPtr(self).Target!;
		new Thread(() => device.Reopen(stream)) { IsBackground = true, Name = "Loom audio reopen" }.Start();
	}
}
