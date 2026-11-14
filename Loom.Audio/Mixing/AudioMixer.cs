using System.Diagnostics;

namespace Loom.Audio.Mixing;

/// <summary>
/// The audio-thread half of an <see cref="AudioEngine"/>: the voices, the buses and the mix. Everything but the
/// constructor runs on the device's thread. The game thread reaches it only through the command queue; it answers
/// through the ended queue (a voice's slot, as the voice finishes), <see cref="Fault"/>, and the stats fields.
/// Nothing here allocates once constructed.
/// </summary>
internal sealed class AudioMixer : IAudioRenderer
{
	/// <summary>The most frames mixed in one pass: a device buffer larger than this is mixed in several.</summary>
	public const int BlockFrames = 256;

	/// <summary>
	/// The span of audio, in seconds, left out of <see cref="MaxRenderLoad"/>: the first renders compile the mix (and any
	/// source's code) as they run, and would otherwise set a peak no later block comes near. A span rather than a count of
	/// callbacks, so it covers the same start-up on a 2 ms buffer as on a 10 ms one.
	/// </summary>
	private const double WarmUpSeconds = 0.25;

	private readonly int _sampleRate;
	private readonly AudioEventQueue<AudioCommand> _commands;
	private readonly AudioEventQueue<int> _ended;
	private readonly Voice[] _voices;
	private readonly Bus[] _buses;
	private readonly float[] _scratchLeft = new float[BlockFrames];
	private readonly float[] _scratchRight = new float[BlockFrames];
	private int _busCount = 1;
	private long _frameTime;

	private long _framesRendered;
	private long _callbacks;
	private int _lastCallbackFrames;
	private float _peak;
	private double _renderLoad;
	private double _maxRenderLoad;

	/// <summary>The first exception a source threw on the audio thread, for the game thread to throw again.</summary>
	public Exception? Fault;

	public AudioMixer(int sampleRate, int maxVoices, int maxBuses, AudioEventQueue<AudioCommand> commands, AudioEventQueue<int> ended)
	{
		_sampleRate = sampleRate;
		_commands = commands;
		_ended = ended;
		_voices = new Voice[maxVoices];
		_buses = new Bus[maxBuses];
		for (var i = 0; i < maxBuses; i++)
			_buses[i] = new Bus();
	}

	public long FramesRendered => Volatile.Read(ref _framesRendered);
	public long Callbacks => Volatile.Read(ref _callbacks);
	public int LastCallbackFrames => Volatile.Read(ref _lastCallbackFrames);
	public float Peak => Volatile.Read(ref _peak);
	public double RenderLoad => Volatile.Read(ref _renderLoad);
	public double MaxRenderLoad => Volatile.Read(ref _maxRenderLoad);

	public void Render(Span<float> output)
	{
		var started = Stopwatch.GetTimestamp();
		var frames = output.Length / 2;
		if ((output.Length & 1) != 0)
			output[^1] = 0f;
		var peak = 0f;
		for (var offset = 0; offset < frames; offset += BlockFrames)
		{
			var n = Math.Min(BlockFrames, frames - offset);
			peak = MathF.Max(peak, RenderBlock(output.Slice(offset * 2, n * 2), n));
		}

		var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
		var load = frames > 0 ? seconds * _sampleRate / frames : 0.0;
		Volatile.Write(ref _peak, peak);
		Volatile.Write(ref _renderLoad, load);
		if (_framesRendered >= _sampleRate * WarmUpSeconds && load > _maxRenderLoad)
			Volatile.Write(ref _maxRenderLoad, load);
		Volatile.Write(ref _lastCallbackFrames, frames);
		Volatile.Write(ref _callbacks, _callbacks + 1);
	}

	/// <summary>Mixes <paramref name="n"/> frames into <paramref name="output"/>; returns the block's peak before clipping.</summary>
	private float RenderBlock(Span<float> output, int n)
	{
		TakeCommands();

		for (var b = 0; b < _busCount; b++)
		{
			_buses[b].Left.AsSpan(0, n).Clear();
			_buses[b].Right.AsSpan(0, n).Clear();
		}

		var info = new AudioRenderInfo(_sampleRate, n, _frameTime);
		for (var i = 0; i < _voices.Length; i++)
		{
			ref var voice = ref _voices[i];
			if (!voice.Active)
				continue;
			var finished = voice.Source is not null ? RenderSource(ref voice, in info) : RenderClip(ref voice, n);
			var mono = voice.Clip is { Channels: 1 };
			MixVoice(ref voice, _buses[voice.Bus], n, mono);
			if (finished || (voice.Stopping && voice.Volume.Current == 0f))
				End(i);
		}

		// Children before parents: a bus's parent always has a lower index, so mixing from the last bus down is in order
		for (var b = _busCount - 1; b > 0; b--)
		{
			var bus = _buses[b];
			ApplyGain(bus, n);
			var parent = _buses[bus.Parent];
			Add(bus.Left, parent.Left, n);
			Add(bus.Right, parent.Right, n);
		}

		var master = _buses[0];
		ApplyGain(master, n);
		var peak = 0f;
		for (var i = 0; i < n; i++)
		{
			var l = master.Left[i];
			var r = master.Right[i];
			peak = MathF.Max(peak, MathF.Max(MathF.Abs(l), MathF.Abs(r)));
			output[i * 2] = Math.Clamp(l, -1f, 1f);
			output[i * 2 + 1] = Math.Clamp(r, -1f, 1f);
		}

		_frameTime += n;
		Volatile.Write(ref _framesRendered, _frameTime);
		return peak;
	}

	private void TakeCommands()
	{
		while (_commands.TryTake(out var command))
		{
			switch (command.Kind)
			{
				case AudioCommandKind.AddBus:
					var added = _buses[command.Index];
					added.Parent = command.Bus;
					added.Volume.SetNow(1f);
					_busCount = Math.Max(_busCount, command.Index + 1);
					break;
				case AudioCommandKind.BusVolume:
					_buses[command.Index].Volume.Set(command.A, command.Frames);
					break;
				case AudioCommandKind.PlayClip:
				case AudioCommandKind.PlaySource:
					ref var started = ref _voices[command.Index];
					started = default;
					started.Active = true;
					started.Bus = command.Bus;
					started.Clip = command.Payload as AudioClip;
					started.Source = command.Payload as AudioSource;
					started.Volume.SetNow(command.A);
					started.Pitch = command.B;
					started.Pan = started.PanTarget = command.C;
					started.Loop = command.Loop;
					break;
				case AudioCommandKind.VoiceVolume:
					ref var faded = ref _voices[command.Index];
					if (faded.Active && !faded.Stopping)
						faded.Volume.Set(command.A, command.Frames);
					break;
				case AudioCommandKind.VoicePitch:
					ref var pitched = ref _voices[command.Index];
					if (pitched.Active)
						pitched.Pitch = command.A;
					break;
				case AudioCommandKind.VoicePan:
					ref var panned = ref _voices[command.Index];
					if (panned.Active)
						panned.PanTarget = command.A;
					break;
				case AudioCommandKind.StopVoice:
					ref var stopped = ref _voices[command.Index];
					if (stopped.Active)
					{
						stopped.Stopping = true;
						stopped.Volume.Set(0f, command.Frames);
					}
					break;
				default:
					throw new InvalidOperationException($"Unknown audio command {command.Kind}.");
			}
		}
	}

	/// <summary>Plays a clip voice's next <paramref name="n"/> frames into the scratch buffers; returns whether it reached its end.</summary>
	private bool RenderClip(ref Voice voice, int n)
	{
		var clip = voice.Clip!;
		var samples = clip.Samples;
		var channels = clip.Channels;
		var frames = clip.Frames;
		var left = _scratchLeft;
		var right = _scratchRight;
		var position = voice.Position;
		var pitch = voice.Pitch;
		var loop = voice.Loop && frames > 0;
		for (var i = 0; i < n; i++)
		{
			if (position >= frames)
			{
				if (!loop)
				{
					left.AsSpan(i, n - i).Clear();
					right.AsSpan(i, n - i).Clear();
					voice.Position = position;
					return true;
				}
				position %= frames;
			}
			var index = (int)position;
			var t = (float)(position - index);
			if (t == 0f)
			{
				left[i] = samples[index * channels];
				if (channels == 2)
					right[i] = samples[index * channels + 1];
			}
			else
			{
				left[i] = Interpolate(samples, channels, 0, index, frames, loop, t);
				if (channels == 2)
					right[i] = Interpolate(samples, channels, 1, index, frames, loop, t);
			}
			position += pitch;
		}
		voice.Position = position;
		return !loop && position >= frames;
	}

	private static float Interpolate(float[] samples, int channels, int channel, int index, int frames, bool loop, float t) =>
		Interpolation.Hermite(
			Interpolation.At(samples, channels, channel, index - 1, frames, loop),
			Interpolation.At(samples, channels, channel, index, frames, loop),
			Interpolation.At(samples, channels, channel, index + 1, frames, loop),
			Interpolation.At(samples, channels, channel, index + 2, frames, loop),
			t);

	/// <summary>Runs a source voice's next block into the scratch buffers; returns whether the source has finished.</summary>
	private bool RenderSource(ref Voice voice, in AudioRenderInfo info)
	{
		var left = _scratchLeft.AsSpan(0, info.Frames);
		var right = _scratchRight.AsSpan(0, info.Frames);
		left.Clear();
		right.Clear();
		try
		{
			return !voice.Source!.RenderBlock(left, right, in info);
		}
		catch (Exception ex)
		{
			// The voice stops and the game thread throws it from AudioEngine.Update: the audio thread must keep going
			Interlocked.CompareExchange(ref Fault, ex, null);
			left.Clear();
			right.Clear();
			return true;
		}
	}

	/// <summary>Adds the scratch buffers into <paramref name="bus"/> at the voice's volume and pan, ramped across the block.</summary>
	private void MixVoice(ref Voice voice, Bus bus, int n, bool mono)
	{
		var v0 = voice.Volume.Current;
		var v1 = voice.Volume.Advance(n);
		var p0 = voice.Pan;
		var p1 = voice.PanTarget;
		voice.Pan = p1;
		var (l0, r0) = mono ? EqualPower(v0, p0) : Balance(v0, p0);
		var (l1, r1) = mono ? EqualPower(v1, p1) : Balance(v1, p1);
		var dl = (l1 - l0) / n;
		var dr = (r1 - r0) / n;
		var left = _scratchLeft;
		var right = mono ? _scratchLeft : _scratchRight;
		var busLeft = bus.Left;
		var busRight = bus.Right;
		for (var i = 0; i < n; i++)
		{
			busLeft[i] += left[i] * (l0 + dl * i);
			busRight[i] += right[i] * (r0 + dr * i);
		}
	}

	// A mono voice keeps its loudness as it pans: -3 dB in each side at the centre, full in one side at either edge
	private static (float Left, float Right) EqualPower(float volume, float pan)
	{
		var angle = (pan + 1f) * (MathF.PI / 4f);
		return (volume * MathF.Cos(angle), volume * MathF.Sin(angle));
	}

	// A stereo voice keeps its own image and turns down the side it pans away from
	private static (float Left, float Right) Balance(float volume, float pan) =>
		(volume * MathF.Min(1f, 1f - pan), volume * MathF.Min(1f, 1f + pan));

	private static void ApplyGain(Bus bus, int n)
	{
		var g0 = bus.Volume.Current;
		var g1 = bus.Volume.Advance(n);
		if (g0 == 1f && g1 == 1f)
			return;
		var dg = (g1 - g0) / n;
		for (var i = 0; i < n; i++)
		{
			var g = g0 + dg * i;
			bus.Left[i] *= g;
			bus.Right[i] *= g;
		}
	}

	private static void Add(float[] from, float[] to, int n)
	{
		for (var i = 0; i < n; i++)
			to[i] += from[i];
	}

	private void End(int slot)
	{
		ref var voice = ref _voices[slot];
		voice.Active = false;
		voice.Clip = null;
		voice.Source = null;
		// Sized to the voice count, and each voice ends once per play, so it can't be full
		_ended.TryPost(slot);
	}

	private struct Voice
	{
		public bool Active;
		public bool Stopping;
		public bool Loop;
		public int Bus;
		public AudioClip? Clip;
		public AudioSource? Source;
		public double Position;
		public float Pitch;
		public float Pan;
		public float PanTarget;
		public GainRamp Volume;
	}

	private sealed class Bus
	{
		public readonly float[] Left = new float[BlockFrames];
		public readonly float[] Right = new float[BlockFrames];
		public int Parent;
		public GainRamp Volume = InitialVolume();

		private static GainRamp InitialVolume()
		{
			var ramp = default(GainRamp);
			ramp.SetNow(1f);
			return ramp;
		}
	}
}
