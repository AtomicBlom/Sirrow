using System.Diagnostics;
using System.Numerics;

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
	private readonly Duck[] _ducks;
	private int _duckCount;
	private readonly Send[] _sends;
	private int _sendCount;
	private GainRamp _output; // after the master bus: the app's focus volume, kept apart from the game's own master volume
							  // The listener, as the game last set it: 3D voices are placed against it every block
	private readonly float _speedOfSound;
	private Vector3 _listener;
	private Vector3 _listenerForward = -Vector3.UnitZ;
	private Vector3 _listenerUp = Vector3.UnitY;
	private Vector3 _listenerRight = Vector3.UnitX;
	private Vector3 _listenerVelocity;
	private int _busCount = 1;
	private long _frameTime;

	private long _framesRendered;
	private long _callbacks;
	private int _lastCallbackFrames;
	private float _peak;
	private double _renderLoad;
	private double _maxRenderLoad;

	/// <summary>The first exception a source or effect threw on the audio thread, for the game thread to throw again.</summary>
	public Exception? Fault;

	public AudioMixer(int sampleRate, int maxVoices, int maxBuses, int maxDucks, int maxEffectsPerBus, int maxSends, float speedOfSound,
			AudioEventQueue<AudioCommand> commands, AudioEventQueue<int> ended)
	{
		_sampleRate = sampleRate;
		_speedOfSound = speedOfSound;
		_commands = commands;
		_ended = ended;
		_voices = new Voice[maxVoices];
		_buses = new Bus[maxBuses];
		for (var i = 0; i < maxBuses; i++)
			_buses[i] = new Bus(maxEffectsPerBus);
		_ducks = new Duck[maxDucks];
		_sends = new Send[maxSends];
		_output.SetNow(1f);
	}

	public long FramesRendered => Volatile.Read(ref _framesRendered);
	public long Callbacks => Volatile.Read(ref _callbacks);
	public int LastCallbackFrames => Volatile.Read(ref _lastCallbackFrames);
	public float Peak => Volatile.Read(ref _peak);
	public double RenderLoad => Volatile.Read(ref _renderLoad);
	public double MaxRenderLoad => Volatile.Read(ref _maxRenderLoad);

	// The game thread starting the highest load over: the audio thread's next render past it sets it again
	public void ResetMaxRenderLoad() => Volatile.Write(ref _maxRenderLoad, 0.0);

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
		UpdateDucks(n);

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
			var doppler = voice.Spatial ? Place(ref voice) : 1f;
			var finished = voice.Source is not null ? RenderSource(ref voice, in info) : RenderClip(ref voice, n, doppler);
			var mono = voice.Clip is { Channels: 1 };
			if (voice.Spatial && !mono)
			{
				// A 3D voice is a point: its two channels heard as one, then panned
				for (var s = 0; s < n; s++)
					_scratchLeft[s] = 0.5f * (_scratchLeft[s] + _scratchRight[s]);
				mono = true;
			}
			MixVoice(ref voice, _buses[voice.Bus], n, mono);
			if (finished || (voice.Stopping && voice.Volume.Current == 0f))
				End(i);
		}

		// Children before parents: a bus's parent always has a lower index, so mixing from the last bus down is in order
		for (var b = _busCount - 1; b > 0; b--)
		{
			var bus = _buses[b];
			FinishBus(bus, n);
			// A send's target was made before its sender, so it has a lower index and is finished later in this loop
			for (var s = 0; s < _sendCount; s++)
			{
				ref var send = ref _sends[s];
				if (send.From == b)
					AddRamped(bus, _buses[send.To], ref send.Level, n);
			}
			var parent = _buses[bus.Parent];
			Add(bus.Left, parent.Left, n);
			Add(bus.Right, parent.Right, n);
		}

		var master = _buses[0];
		FinishBus(master, n);
		Ramp(master.Left, master.Right, ref _output, n);
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
					// A 3D play's settings and position came just before it: keep them through the reset
					var placement = started.Settings;
					var at = started.At;
					var velocity = started.Velocity;
					started = default;
					if (command.Spatial)
					{
						started.Spatial = true;
						started.Settings = placement;
						started.At = at;
						started.Velocity = velocity;
					}
					started.DistanceGain = started.DistanceGainTarget = 1f;
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
				case AudioCommandKind.AddDuck:
					var settings = (DuckSettings)command.Payload!;
					_ducks[command.Index] = new Duck { Settings = settings, Enabled = true };
					_buses[settings.Trigger].IsTrigger = true;
					_duckCount = Math.Max(_duckCount, command.Index + 1);
					break;
				case AudioCommandKind.DuckEnabled:
					_ducks[command.Index].Enabled = command.A != 0f;
					break;
				case AudioCommandKind.OutputVolume:
					_output.Set(command.A, command.Frames);
					break;
				case AudioCommandKind.ListenerPosition:
					_listener = new Vector3(command.A, command.B, command.C);
					break;
				case AudioCommandKind.ListenerForward:
					_listenerForward = new Vector3(command.A, command.B, command.C);
					_listenerRight = Vector3.Normalize(Vector3.Cross(_listenerForward, _listenerUp));
					break;
				case AudioCommandKind.ListenerUp:
					_listenerUp = new Vector3(command.A, command.B, command.C);
					_listenerRight = Vector3.Normalize(Vector3.Cross(_listenerForward, _listenerUp));
					break;
				case AudioCommandKind.ListenerVelocity:
					_listenerVelocity = new Vector3(command.A, command.B, command.C);
					break;
				case AudioCommandKind.VoiceSpatial:
					_voices[command.Index].Settings = new AudioSpatial
					{
						MinDistance = command.A,
						MaxDistance = command.B,
						RolloffFactor = command.C,
						DopplerFactor = command.D,
						Rolloff = (AudioRolloff)command.Frames,
					};
					break;
				case AudioCommandKind.VoicePosition:
					_voices[command.Index].At = new Vector3(command.A, command.B, command.C);
					break;
				case AudioCommandKind.VoiceVelocity:
					_voices[command.Index].Velocity = new Vector3(command.A, command.B, command.C);
					break;
				case AudioCommandKind.AddEffect:
					var target = _buses[command.Index];
					target.Effects[target.EffectCount++] = (AudioEffect)command.Payload!;
					break;
				case AudioCommandKind.AddSend:
					ref var sent = ref _sends[command.Index];
					sent.From = command.Bus;
					sent.To = command.Frames;
					sent.Level.SetNow(command.A);
					_sendCount = Math.Max(_sendCount, command.Index + 1);
					break;
				case AudioCommandKind.SendLevel:
					_sends[command.Index].Level.Set(command.A, command.Frames);
					break;
				default:
					throw new InvalidOperationException($"Unknown audio command {command.Kind}.");
			}
		}
	}

	// Places a 3D voice for the listener as they are now: its pan and distance gain, ramped across the block from where
	// they were (or set outright on its first block), and its Doppler pitch factor, which it returns
	private float Place(ref Voice voice)
	{
		var (gain, pan, doppler) = Spatializer.Compute(_listener, _listenerRight, _listenerVelocity, voice.At, voice.Velocity, in voice.Settings, _speedOfSound);
		if (!voice.Placed)
		{
			voice.DistanceGain = gain;
			voice.Pan = pan;
			voice.Placed = true;
		}
		voice.DistanceGainTarget = gain;
		voice.PanTarget = pan;
		return doppler;
	}

	/// <summary>
	/// Plays a clip voice's next <paramref name="n"/> frames into the scratch buffers, its pitch times
	/// <paramref name="pitchScale"/> (a 3D voice's Doppler shift); returns whether it reached its end.
	/// </summary>
	private bool RenderClip(ref Voice voice, int n, float pitchScale)
	{
		var clip = voice.Clip!;
		var samples = clip.Samples;
		var channels = clip.Channels;
		var frames = clip.Frames;
		var left = _scratchLeft;
		var right = _scratchRight;
		var position = voice.Position;
		var pitch = voice.Pitch * pitchScale;
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

	/// <summary>
	/// Adds the scratch buffers into <paramref name="bus"/> at the voice's volume (times a 3D voice's distance gain) and pan,
	/// ramped across the block.
	/// </summary>
	private void MixVoice(ref Voice voice, Bus bus, int n, bool mono)
	{
		var v0 = voice.Volume.Current * voice.DistanceGain;
		var v1 = voice.Volume.Advance(n) * voice.DistanceGainTarget;
		voice.DistanceGain = voice.DistanceGainTarget;
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

	// Each duck moves its depth toward full while its trigger bus was heard in the last block (or within the hold after), and
	// back toward 0 dB otherwise, at its attack and release rates; a bus several ducks turn down takes all their gains. The
	// levels are the last block's, so the order the buses were made in doesn't matter, for one block's delay (a few ms)
	// against attacks of tens of milliseconds
	private void UpdateDucks(int n)
	{
		for (var b = 0; b < _busCount; b++)
		{
			var bus = _buses[b];
			bus.DuckFrom = bus.DuckTo;
			bus.DuckTo = 1f;
		}
		for (var d = 0; d < _duckCount; d++)
		{
			ref var duck = ref _ducks[d];
			var settings = duck.Settings!;
			var heard = duck.Enabled && _buses[settings.Trigger].Level > settings.Threshold;
			duck.HoldLeft = heard ? settings.HoldFrames : Math.Max(0, duck.HoldLeft - n);
			var depth = heard || (duck.Enabled && duck.HoldLeft > 0) ? settings.DepthDb : 0f;
			duck.Db = depth < duck.Db
				? MathF.Max(depth, duck.Db - settings.AttackDbPerFrame * n)
				: MathF.Min(depth, duck.Db + settings.ReleaseDbPerFrame * n);
			if (duck.Db < 0f)
				_buses[settings.Target].DuckTo *= MathF.Pow(10f, duck.Db / 20f);
		}
	}

	// A bus whose voices, children and sends are all in: its effects, its volume, its level (if a duck listens to it), then
	// its ducks. The effects come first so a reverb's tail is turned down by the bus's volume with everything else. The
	// level is taken before the ducks, so two buses ducking each other don't hold each other down
	private void FinishBus(Bus bus, int n)
	{
		if (bus.EffectCount > 0)
		{
			var left = bus.Left.AsSpan(0, n);
			var right = bus.Right.AsSpan(0, n);
			for (var e = 0; e < bus.EffectCount; e++)
			{
				var effect = bus.Effects[e]!;
				if (effect.Bypass || effect.Faulted)
					continue;
				try
				{
					effect.Run(left, right);
				}
				catch (Exception ex)
				{
					// The effect is skipped from now on and the game thread throws it from AudioEngine.Update
					Interlocked.CompareExchange(ref Fault, ex, null);
					effect.Faulted = true;
				}
			}
		}
		Ramp(bus.Left, bus.Right, ref bus.Volume, n);
		if (bus.IsTrigger)
		{
			var level = 0f;
			for (var i = 0; i < n; i++)
				level = MathF.Max(level, MathF.Max(MathF.Abs(bus.Left[i]), MathF.Abs(bus.Right[i])));
			bus.Level = level;
		}
		Ramp(bus.Left, bus.Right, bus.DuckFrom, bus.DuckTo, n);
	}

	private static void Ramp(float[] left, float[] right, ref GainRamp gain, int n)
	{
		var g0 = gain.Current;
		Ramp(left, right, g0, gain.Advance(n), n);
	}

	private static void Ramp(float[] left, float[] right, float g0, float g1, int n)
	{
		if (g0 == 1f && g1 == 1f)
			return;
		var dg = (g1 - g0) / n;
		for (var i = 0; i < n; i++)
		{
			var g = g0 + dg * i;
			left[i] *= g;
			right[i] *= g;
		}
	}

	private static void Add(float[] from, float[] to, int n)
	{
		for (var i = 0; i < n; i++)
			to[i] += from[i];
	}

	// Adds a bus into another at a gain ramped across the block
	private static void AddRamped(Bus from, Bus to, ref GainRamp level, int n)
	{
		var g0 = level.Current;
		var g1 = level.Advance(n);
		if (g0 == 0f && g1 == 0f)
			return;
		var dg = (g1 - g0) / n;
		for (var i = 0; i < n; i++)
		{
			var g = g0 + dg * i;
			to.Left[i] += from.Left[i] * g;
			to.Right[i] += from.Right[i] * g;
		}
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

		// A 3D voice: placed for the listener every block, which sets its pan, its distance gain and its pitch factor
		public bool Spatial;
		public bool Placed; // placed at least once: until then its first block starts where it is, not ramping in from the centre
		public AudioSpatial Settings;
		public Vector3 At;
		public Vector3 Velocity;
		public float DistanceGain;
		public float DistanceGainTarget;
	}

	private sealed class Bus(int maxEffects)
	{
		public readonly float[] Left = new float[BlockFrames];
		public readonly float[] Right = new float[BlockFrames];
		public int Parent;
		public GainRamp Volume = InitialVolume();
		public bool IsTrigger;    // a duck listens to it, so its level is taken
		public float Level;       // its last block's peak, after its volume, before its ducks
		public float DuckFrom = 1f;
		public float DuckTo = 1f; // the gain its ducks ramp it across the block, from DuckFrom
		public readonly AudioEffect?[] Effects = new AudioEffect?[maxEffects];
		public int EffectCount;   // run in the order added, before its volume

		private static GainRamp InitialVolume()
		{
			var ramp = default(GainRamp);
			ramp.SetNow(1f);
			return ramp;
		}
	}

	private struct Duck
	{
		public DuckSettings? Settings;
		public bool Enabled;
		public int HoldLeft;
		public float Db; // how far down it has the target now: 0 to Settings.DepthDb
	}

	// A share of one bus mixed into another, besides its parent
	private struct Send
	{
		public int From;
		public int To;
		public GainRamp Level;
	}
}
