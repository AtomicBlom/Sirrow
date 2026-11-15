# Audio

How to make sound in a Loom app: clips played on voices, mixed through buses, and **sources**, sound the game writes
itself sample by sample (software synthesizers, procedural engine notes, generated music). Then what runs on which
thread, and what each platform does underneath.

[`Loom.Audio`](../Loom.Audio) holds the mixer and depends only on the BCL, as `Loom.Input` does. Each host opens its
platform's output device behind one seam, `IAudioDevice`:

| Host | Device | Where the mix runs | Latency the mix adds |
|---|---|---|---|
| Desktop (`Loom.Hosting.Desktop`) | SDL 3, the system's default output | SDL's audio thread | one device buffer: 480 frames (10 ms) on Windows, the floor of shared-mode WASAPI |
| Android (`Loom.Hosting.Android`) | AAudio, low-latency, exclusive where the device allows | AAudio's callback thread | two bursts: 192 frames (4 ms) on a Galaxy S23 Ultra's exclusive MMAP path |
| Browser (`Loom.Hosting.Web`) | an AudioWorklet | the frame thread, 50 ms ahead of the speaker | the 50 ms lead, by design (see [The browser](#the-browser)) |

`samples/HelloAudio` is a whole app built on all of it: an eight-voice synthesizer played from the keyboard, an
arpeggiator timed on the audio clock, and a clip fired from the game. It builds as a desktop exe, an Android APK and a
browser app from the same source.

Sound built on top of the mixer (rendering offline to measure it or save it as a WAV file) is in
[`Loom.Audio.Synth`](../Loom.Audio.Synth), and [the synthesis guide](synth.md) covers it.

---

## 1. Turning it on

Set `AppOptions.Audio` in `Configure`, then play through `LoomApplication.Audio`:

```csharp
sealed class MyGame : LoomApplication
{
	private AudioBus _sfx;
	private AudioClip _hit = null!;

	protected override void Configure(AppOptions options) =>
		options.Audio = new AudioOptions(); // the device's own rate and its smallest buffer

	protected override void LoadContent()
	{
		_sfx = Audio.CreateBus("sfx");
		_hit = Audio.LoadWav(ReadContent("sounds/hit.wav"));
	}

	protected override void Update(FrameTime time)
	{
		if (Input.Pressed(Key.Space))
			Audio.Play(_hit, _sfx, volume: 0.8f, pitch: 1.1f, pan: -0.3f);
	}
}
```

`LoomApplication` runs the engine through the app's lifecycle, so an app never opens, pumps or closes it:

| When | What happens |
|---|---|
| Before `LoadContent` | The host opens its output device and the app's `AudioEngine` starts it. The log names the backend, the output, the rate and the buffer granted. |
| Every tick, after `Update` and `BeforeRender` | `Audio.Update()`: frees the voices that ended and throws what a source threw on the audio thread. |
| `OnSuspending` / `OnResumed` | The device pauses after `OnSuspending` and resumes before `OnResumed`. Voices hold where they are. |
| The window or tab loses or regains focus | `Audio.SetFocused`, before `OnFocusChanged`: the output fades to `AudioOptions.UnfocusedVolume` and back (see below). |
| After `UnloadContent` | The engine closes the device. |

With `AppOptions.Audio` left null, audio is off and reading `Audio` throws.

### `AudioOptions`

Every value is a request the platform meets as closely as it can. `Audio.Device` says what was granted.

| Option | Default | Meaning |
|---|---|---|
| `SampleRate` | 0 (the device's own) | The mixing rate. Any rate but the device's own makes the platform resample, which adds latency. |
| `BufferFrames` | 0 (the smallest the backend offers) | The device buffer, which is the latency the mixer adds. Raise it only if the output crackles on a slow device. In the browser it is the lead ahead of the speaker. |
| `MaxVoices` | 64 | Voices playing at once, clips and sources together. A play past it is dropped (see [Voices](#3-voices)). |
| `MaxBuses` | 16 | Buses, the master bus included. |
| `MaxDucks` | 8 | Ducks (see [Ducking](#ducking)). |
| `SpeedOfSound` | 343 | The speed of sound in the game's units a second, for the Doppler shift of 3D voices: 343 for metres, 1125 for feet. |
| `UnfocusedVolume` | 1 (no change) | The output's volume while the app is out of focus: another window in front, the browser tab hidden, the app behind another on Android. The whole mix fades to it over a quarter second and back on return. 0 silences an app in the background. It is a gain of its own after the master bus, so the game's volumes, the master's included, are left alone, and it multiplies with them. `Audio.SetUnfocusedVolume` changes it while the app runs, for a setting the player chooses. |
| `CommandCapacity` | 4096 | Commands (plays, stops, volume changes) waiting for the audio thread at once. Past it the engine throws rather than drop one. |

### No output

When a host can't open an output (no device, or SDL3 missing on the desktop), it hands the engine a `NullAudioDevice`
and logs a warning saying why. A `NullAudioDevice` renders in real time and drops what it renders, so voices still
advance and end, and a game that waits on a sound finishing behaves as it would with a speaker. A machine with no
speaker is not a bug in the app, so this is not an error.

---

## 2. Clips

A clip is sound decoded into memory, ready to play any number of times at once: a footstep, a hit, a loop.

```csharp
var hit = Audio.LoadWav(ReadContent("sounds/hit.wav"));            // a WAV file's bytes
var step = Audio.LoadOgg(ReadContent("sounds/step.ogg"));          // an Ogg Vorbis file's bytes
var bell = Audio.LoadFlac(ReadContent("sounds/bell.flac"));        // a FLAC file's bytes
var beep = Audio.CreateClip(samples, channels: 1, sampleRate: 44_100); // samples the app made, -1..1
```

- `LoadWav` reads RIFF/WAVE files: PCM at 8, 16, 24 or 32 bits, and IEEE float at 32 or 64, mono or stereo
  (`WAVE_FORMAT_EXTENSIBLE` included). Any other format throws `NotSupportedException`, and bytes that aren't a WAV
  file throw `InvalidDataException`.
- `LoadOgg` decodes an Ogg Vorbis file whole, mono or stereo. A file with more channels throws
  `NotSupportedException`, and bytes that aren't Ogg Vorbis, or are damaged, throw `InvalidDataException`. For music,
  stream instead (below): a clip holds every decoded sample, ten times the file's size or more.
- `LoadFlac` decodes a FLAC file whole, mono or stereo, at any bit depth from 4 to 32. FLAC is lossless, at about half
  the size of the same audio as WAV: for sounds whose detail Vorbis would smear. The same exceptions apply.
- All of them convert the clip to the engine's rate as it loads, so playing it never resamples (a pitch other than 1 aside).
  The conversion is a 4-point Hermite curve, which doesn't filter on the way down: a 96 kHz clip loaded at 48 kHz can
  alias faintly. Supply clips at the engine's rate where that matters.
- **Any thread may make a clip.** Decode on a loading thread (`ReadContentAsync`) and play on the frame thread.
- A clip belongs to the engine whose rate it was made at. Playing it on an engine at another rate throws.

### Streams

Music is streamed: decoded as it plays, from an Ogg Vorbis or FLAC file held compressed. `OpenStream` tells which from
the file's first bytes. An `AudioStream` is a source, so it plays, fades, pans and stops like one.

```csharp
_theme = Audio.OpenStream(ReadContent("music/theme.ogg"), loop: true);
Audio.Play(_theme, music);
// ... later
Audio.Stop(_theme, fadeSeconds: 2f);
_theme.Dispose(); // once it has faded, or when the game is done with it
```

- **Each play starts from the beginning.** A looping stream joins its end to its start without a gap, to the sample:
  the file's length is exactly what was encoded (an Ogg file's last page's granule position says so; a FLAC file's
  frames hold exactly its samples).
- **Decoding never happens on the audio thread.** A streaming thread of the engine's decodes about half a second
  ahead into a lock-free ring, waking every 10 ms, and the audio thread reads the ring. In the browser, where the mix
  runs on the frame thread, `Audio.Update()` decodes ahead instead. A play decodes the first 100 ms on the calling
  thread, so the stream has sound from its first block.
- **A file at another rate is converted as it plays,** by the same curve clips are converted with. A 44.1 kHz file
  plays at the right pitch and speed on a 48 kHz device.
- `OpenStream(byte[])` takes the file's bytes, as `ReadContent` gives them. `OpenStream(Stream)` reads a file stream as
  it plays, off the audio thread; a stream that can't seek (an Android asset's) is read into memory first, so that it
  can loop and play again. The `AudioStream` owns the stream and closes it.
- Opening a file checks its headers, so a file that is neither Ogg Vorbis nor FLAC throws there. Damage further in
  throws `InvalidOperationException` (the decoder's exception inside it) from the next `Audio.Update()`, and the stream
  stops.
- Decoding costs little: Vorbis and FLAC both decode at over 250 times real time for 44.1 kHz stereo on a Snapdragon X
  core, so a stream takes well under 1% of a core.
- `Dispose` stops a playing stream at once and closes its file. Disposing the engine closes every stream.

The decoder is Loom's own, written from the Vorbis I specification: no native library, the same code on every
platform, and it allocates nothing once a stream is open. It decodes what every encoder since libvorbis 1.0 writes,
matching libvorbis to its last bit of 16-bit output (`VorbisDecoderTests`); floor type 0, which nothing has written
since 2002, throws `NotSupportedException`.

The FLAC decoder is Loom's own too, written from RFC 9639, and also allocates nothing once open. It is bit-exact: its
tests check every decoded sample against the MD5 each file carries (`FlacDecoderTests`), and it decodes all of the
IETF working group's conformance files with samples to check, 32-bit and 768 kHz included. It checks every frame's
checksums, so a damaged file throws rather than playing noise. It skips ID3 tags before and after the audio. It
refuses (`NotSupportedException`) a file that changes its rate or channel count partway, and doesn't read FLAC inside
Ogg or a file that starts without its `fLaC` header.

---

## 3. Voices

Every play takes a voice from a fixed pool and returns a `VoiceHandle`:

```csharp
var engineLoop = Audio.Play(_engineClip, _sfx, loop: true);
Audio.SetPitch(engineLoop, 0.8f + rpm / 8000f);
Audio.SetVolume(engineLoop, 0.5f, fadeSeconds: 0.2f);
Audio.SetPan(engineLoop, -0.4f);
Audio.Stop(engineLoop, fadeSeconds: 0.5f);
```

| Member | What it does |
|---|---|
| `Play(clip, bus, volume, pitch, pan, loop)` | Plays a clip. `pitch` 2 is an octave up and twice as fast. `pan` runs -1 (left) to 1 (right). A looping clip plays until stopped. |
| `Play(source, bus, volume, pan)` | Plays a source (see [Sources](#5-sources)) until it finishes or is stopped. |
| `SetVolume(voice, volume, fadeSeconds)` | Moves a voice's volume, a linear gain. |
| `SetPitch(voice, pitch)` | A clip voice's pitch. A source voice ignores it. |
| `SetPan(voice, pan)` | Moves the pan across the next block. |
| `Stop(voice, fadeSeconds)` / `Stop(source, fadeSeconds)` | Fades the voice out, then ends it. |
| `IsPlaying(voice)` | Whether the voice still plays, as the frame thread last heard (at most a frame old). |

Things to know:

- **Nothing clicks.** Every change of volume, pan or bus volume ramps across at least one block (256 frames at most,
  about 5 ms at 48 kHz). A stop with no fade still ramps to silence over one block.
- **A handle goes stale when its voice ends,** and the engine ignores a stale handle rather than reach the voice that
  took its slot. A game can keep handles without tracking when each sound finishes.
- **When every voice is busy, a play is dropped.** It returns `VoiceHandle.None` and adds one to
  `Stats.DroppedVoices`. Raise `AudioOptions.MaxVoices` if that happens in normal play.
- A mono clip pans at equal power (-3 dB in each side at the centre). A stereo clip or a source pans as a balance: it
  keeps its own image and turns down the side it pans away from.

### Voices in 3D

A voice played with `PlayAt` has a position instead of a pan. Each block, the engine places it for the listener: panned
by which side of the listener it is on, quieter with distance, and pitch-shifted by motion (Doppler).

```csharp
// Each frame, from the game's camera (the engine takes positions, never a camera)
Audio.SetListener(camera.Position, camera.Forward, camera.Up, cameraVelocity);

var engineHum = Audio.PlayAt(_humClip, car.Position, _sfx, loop: true,
	spatial: new AudioSpatial { MinDistance = 2f, MaxDistance = 80f });
Audio.SetPosition(engineHum, car.Position, car.Velocity);  // each frame it moves
Audio.PlayAt(_footstep, foot.Position, _sfx);                // a one-shot where it happened
```

| Member | What it does |
|---|---|
| `SetListener(position, forward, up, velocity)` | Where the game hears from. Call it each frame the listener moves. Until it is set, the listener is at the origin facing -Z, with +Y up. |
| `PlayAt(clip, position, bus, volume, pitch, loop, spatial)` | Plays a clip as a 3D voice. |
| `PlayAt(source, position, bus, volume, spatial)` | Plays a source (a stream included) as a 3D voice. |
| `SetPosition(voice, position, velocity)` | Moves a 3D voice, heard from the next block. |

`AudioSpatial` sets how a voice falls off and shifts. Its distances are in the game's units, as its positions are:

| Property | Default | Meaning |
|---|---|---|
| `MinDistance` | 1 | Within this, full volume. |
| `MaxDistance` | 100 | Beyond this, no quieter. |
| `Rolloff` | `Inverse` | The curve between the two: `Inverse` (halves at twice the minimum distance; how sound falls off in the open), `Linear` (a straight fade to silence at the maximum distance), or `Exponential`. They are OpenAL's clamped models. |
| `RolloffFactor` | 1 | How steep the curve is: 0 doesn't fall off, 2 is twice as steep. |
| `DopplerFactor` | 1 | How much motion shifts the pitch: 0 never, 1 as sound really does, more to exaggerate it. |

- **Right-handed,** as `Matrix4x4.CreateLookAt` is: the listener's right is forward × up.
- **Stereo panning only:** a voice's pan is how far to the listener's right it is. Two speakers can't tell front from
  back, so a voice behind the listener sounds where its mirror in front would. There is no HRTF.
- **A 3D voice is one point.** A stereo clip or source is heard with its two channels mixed, then panned at equal power.
- **Doppler comes from the velocities the game gives,** the voice's and the listener's, not from how far they moved
  between frames, which would jitter with the frame rate. A voice given no velocity isn't shifted. The shift is held
  within two octaves either way. A source has no pitch, so it isn't shifted.
- **Volume and pitch still apply:** `SetVolume` scales the voice under its distance gain, and `SetPitch` multiplies with
  the Doppler shift. `SetPan` on a 3D voice throws, as does `SetPosition` on a voice played with `Play`.
- Everything moves smoothly: the distance gain and the pan ramp across each block, and a 3D voice is placed before its
  first block, so it never starts at the centre at full volume.

---

## 4. Buses

Buses group voices so they can be set as one: music, sound effects, UI. They form a tree under `Audio.Master`, each
mixed into its parent.

```csharp
var music = Audio.CreateBus("music");
var sfx = Audio.CreateBus("sfx");
var footsteps = Audio.CreateBus("footsteps", parent: sfx);

Audio.SetBusVolume(music, 0.3f, fadeSeconds: 1f); // everything in music follows it
```

- Make buses at load. There are at most `AudioOptions.MaxBuses`, and none is ever removed.
- The engine names only the master bus. Every other bus, and its name, is the app's.
- The mix order follows from the tree (children before parents), so an app never orders buses itself.
- The master bus is clipped to -1..1 on its way to the device. `Stats.Peak` reports the loudest sample before that
  clip, so a peak above 1 means the mix is too loud.

### Ducking

A duck turns one bus down while another has sound on it: music under dialogue, ambience under a cutscene.

```csharp
var dialogue = Audio.CreateBus("dialogue");
var music = Audio.CreateBus("music");
var duck = Audio.AddDuck(trigger: dialogue, target: music, depthDb: -9f, attackSeconds: 0.05f, releaseSeconds: 0.4f);

Audio.SetDuckEnabled(duck, false); // music back up, however loud the dialogue
```

| Parameter | Default | Meaning |
|---|---|---|
| `depthDb` | (required) | How far down the target goes, in decibels, below zero: -6 halves it, -12 is a quarter. |
| `attackSeconds` | 0.05 | How long the target takes to go all the way down. |
| `releaseSeconds` | 0.4 | How long it takes to come all the way back. |
| `thresholdDb` | -40 | The trigger's level, in decibels of full scale, above which it counts as sounding. |
| `holdSeconds` | 0.15 | How long the target stays down after the trigger falls quiet, so it doesn't pump up between words. |

- **A duck follows the trigger's level, not its voices.** The target comes back up in a pause in the dialogue, and a
  source playing silence ducks nothing. The level is the trigger bus's peak after its own volume, so turning the
  dialogue bus down to nothing stops it ducking.
- **The duck acts a block after it hears.** Each block's ducks are worked out from the last block's levels, so the order
  buses were made in doesn't matter. The block of delay (a few milliseconds) sits inside any attack.
- A bus several ducks turn down takes all their depths: two -6 dB ducks sounding at once make -12 dB.
- **A duck can't involve a bus that contains the other.** Ducking a bus the trigger is mixed into (the master, say) would
  turn the trigger down too, and ducking a bus mixed into the trigger would lower the trigger's level and let the duck
  go, so it would pump. Both throw.
- Make ducks at load, like buses. There are at most `AudioOptions.MaxDucks`, and none is removed: `SetDuckEnabled`
  turns one off, and its target comes back up at its release rate.

---

## 5. Sources

A source is sound the game writes itself. Subclass `AudioSource`, write `Render`, and play it like a clip:

```csharp
sealed class Sine : AudioSource
{
	public AudioParameter Frequency { get; } = new(440f);
	private float _phase, _rate;

	// The frame thread, as the source starts playing: size buffers and work out coefficients here
	protected override void OnStart(int sampleRate) => _rate = sampleRate;

	// The audio thread, once a block
	protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
	{
		var frequency = Frequency.Ramp(info.Frames); // glides across the block: no click when it changes
		for (var i = 0; i < info.Frames; i++)
		{
			left[i] = right[i] = 0.2f * MathF.Sin(2 * MathF.PI * _phase);
			_phase = (_phase + frequency.Next() / _rate) % 1f;
		}
		return true; // false ends the voice
	}
}

var sine = new Sine();
Audio.Play(sine, music);
sine.Frequency.Value = 660f; // from the game, any time
```

### The rules for `Render`

`Render` runs on the audio thread, once a block of up to 256 frames, against a deadline: a block late is heard as a
click or a dropout.

- **Don't allocate.** A garbage collection pauses every managed thread, the audio thread included, so an allocation in
  `Render` is the one thing in the source's control that can stall the mix. Allocate in the constructor or `OnStart`.
- **Don't lock, block or wait,** and don't call anything that might (I/O, logging, `Console`).
- The spans arrive zeroed and hold `info.Frames` samples each. Write both, even for a mono sound.
- Return false once the source has finished. Its voice ends and the source can be played again.
- **An exception thrown in `Render` stops the voice** and is thrown again on the frame thread from the next
  `Audio.Update()`, wrapped in an `InvalidOperationException`. The audio thread itself carries on.

`AudioRenderInfo` carries the block's `Frames`, the engine's `SampleRate`, and `FrameTime`: how many frames the engine
had rendered before this block. `FrameTime` is the audio clock, so a sequencer or arpeggiator counts beats on it and
stays sample-accurate however the frame rate wanders. It runs only while the device does.

### One voice at a time

A source keeps its own state, so it plays on one voice at a time, and playing it while it plays throws. It counts as
playing from its `Play` until the engine hears its voice end, which after `Stop` is once the audio thread has rendered
the fade. So `Stop(source)` followed at once by `Play(source)` throws. Play it again once `source.IsPlaying` is false,
or make another instance. Each `Play` of a source hears the voices that have ended, so a source that finished or faded
out since the last frame can be played again straight away.

### Steering a source from the game

The game and the audio thread share no lock, so a source takes its controls through two lock-free types:

| Type | For | Game thread | Audio thread |
|---|---|---|---|
| `AudioParameter` | A continuous value where only the latest matters: a cutoff, an RPM, an intensity. | Set `Value` whenever it likes. | `Ramp(info.Frames)` once a block, then `Next()` once a sample: a straight line from where it was to `Value`. `Snap()` jumps straight there, for a control where a step is what's wanted (a waveform choice). |
| `AudioEventQueue<T>` | Discrete events that must all arrive, in order: note on, note off, a trigger. `T` is a small struct. | `TryPost(event)` (false when full), or `Post`, which throws when full. | `TryTake(out event)` at the start of `Render`, until it returns false. |

An `AudioEventQueue<T>` has a fixed capacity (rounded up to a power of two) and is strictly one thread posting and one
taking. Two threads posting to the same queue corrupt it.

```csharp
readonly record struct NoteEvent(int Note, float Velocity, bool On);

sealed class Synth : AudioSource
{
	public AudioEventQueue<NoteEvent> Notes { get; } = new(256);

	protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
	{
		while (Notes.TryTake(out var note))
		{
			if (note.On)
				NoteOn(note.Note, note.Velocity);
			else
				NoteOff(note.Note);
		}
		// ... render the voices
		return true;
	}
}

synth.Notes.TryPost(new NoteEvent(60, 0.9f, On: true)); // from Update
```

---

## 6. Threads

Two threads share an `AudioEngine`:

- **The frame thread** (the thread that made the engine, the app's in a `LoomApplication`) calls every method. A call
  from any other thread throws, except making clips (`CreateClip`, `LoadWav`), which touches no shared state.
- **The audio thread** (the device's) mixes. It never waits on the game.

Each call on the frame thread copies a command into a lock-free queue, and the audio thread takes the queued commands at
the start of its next block. So a play is heard within one device buffer of the call, and the order of calls is the
order they take effect. The audio thread answers through a second queue (the voices that ended, which `Update` frees)
and through `Stats`.

Once running, neither side allocates: not a play, a stop, a volume change, or a block of the mix.
`AudioEngineTests.The_mix_allocates_nothing_once_running` holds the engine to that. The game's own allocations still
matter: a garbage collection the game sets off pauses the audio thread too. A game that keeps its steady frame free of
allocations, as Loom's render loop is, keeps its audio safe as well.

### Watching the mix

`Audio.Stats` reports how the mix is doing, for a debug display or a test:

| Field | Meaning |
|---|---|
| `FramesRendered`, `Callbacks`, `LastCallbackFrames` | The mix's clock, how many times the device has asked for samples, and the buffer it is really running at. |
| `ActiveVoices`, `DroppedVoices` | Voices playing now, and plays dropped because every voice was busy. |
| `Peak` | The loudest sample of the last buffer, before the clip to -1..1. |
| `RenderLoad`, `MaxRenderLoad` | How long the last render took, as a fraction of the time its samples last (0.1 is 10% of the deadline), and the highest since the first quarter second. Near 1, the output drops out. |

---

## 7. Platforms

### The desktop

The desktop host opens the system's default output through SDL 3, at the device's own rate in 32-bit float stereo, and
asks for a 128-frame buffer. SDL rounds it to what the platform's audio API takes: on Windows that is shared-mode
WASAPI's period, 480 frames (10 ms) at 48 kHz. An output on the default device follows the system's default as it
changes (headphones plugged in), which SDL does on its own.

SDL 3 is the same `SDL3.dll` the host reads gamepads with, fetched by `Sdl3.targets` (see
[api-vocabulary.md](api-vocabulary.md#input-on-the-desktop-loomhostingdesktop)). The gamepad pump and the audio device
each start and stop only their own SDL subsystem, so closing one never stops the other. Where SDL3 is missing or won't
load, the app runs silent on a `NullAudioDevice`.

### Android

The Android host opens an AAudio output stream: low-latency performance mode, exclusive sharing where the device
offers it (the MMAP path, straight into the hardware's buffer), usage `GAME`, and a buffer of two bursts, the floor
AAudio's own guidance gives for glitch-free low latency. The mixer renders straight into AAudio's buffer on AAudio's
high-priority callback thread.

- **An exclusive stream can be taken away before it starts.** AAudio reports it disconnected. The device reopens it and
  starts again, and if exclusive use is taken a second time, opens a shared stream instead.
- **When the output changes** (headphones unplugged, a Bluetooth device dropping), AAudio ends the stream. The device
  reopens one on the new output, at the engine's rate, from a thread of its own (AAudio forbids reopening from its
  callbacks). Where the new output won't run at the engine's rate, the app goes silent and logs a warning, since the
  engine's clips were made at the old rate.
- **AAudio calls C# through a native shim.** Android builds `libaaudio` with Control-Flow Integrity, which crashes
  (SIGSEGV in `__cfi_slowpath`) on a callback whose code lies outside any loaded library, and a C#
  `[UnmanagedCallersOnly]` entry point is a runtime stub outside every library. So AAudio calls two forwarding
  functions in `libloomaaudio.so` (`Loom.Hosting.Android/Native/loom_aaudio.c`), built without CFI, and they call the
  C# callbacks. The built libraries for `arm64-v8a` and `x86_64`, linked for 16 KB pages, are checked in under
  `Native/lib/`, so building Loom needs no NDK. Rebuild them with `pwsh Loom.Hosting.Android/Native/build.ps1` after
  changing `loom_aaudio.c`.

### The browser

The .NET runtime can't run on the browser's audio thread, so the browser host turns the usual arrangement around. The
mix runs on the frame thread, once a frame, after the app's tick. It tops up an AudioWorklet's queue to a lead of 50 ms
ahead of the speaker (`AudioOptions.BufferFrames` sets it), and the worklet plays from that queue on the browser's
audio thread. The lead is the web's latency, and it is what rides out a late frame: a frame later than the lead is
heard as a gap.

Because the mix runs on the frame thread, a source's `Render` runs there too. The rules for `Render` and the lock-free
types work the same, so a source written for the desktop runs unchanged.

- **A page makes sound only after the player's first click, tap or key.** The host listens for it and resumes the
  AudioContext. Until then the mix keeps time (voices advance and end) and sends nothing.
- When the frame loop stalls (a background tab), the mix skips the time it missed, a quarter second at most, rather
  than rendering the whole gap at once.
- `__loomAudio()` at the browser's console reports the output's state, its rate and latencies, and the frames sent and
  played (sent minus played is how far the mix runs ahead of the speaker).
- An embedder that boots the .NET runtime itself must register the `loom-audio` JS module beside `loom-webgpu` (see
  `loom-boot.js`).

### A host of your own, and tests

`IHostControl.OpenAudioDevice(AudioOptions)` is how a host hands the app its output. The default returns a
`NullAudioDevice`, so a host with no audio needs nothing. A host with an output implements `IAudioDevice`: once started, it asks the
renderer for interleaved stereo 32-bit float at its `SampleRate`, on a thread of its own (or, as the browser's does,
whenever the host pumps it), and it pauses and resumes on request.

An `AudioEngine` also works on its own, over any `IAudioDevice`. `OfflineAudioDevice` renders only when asked, on the
caller's thread: for tests, for rendering to a file faster than real time, and for a host that pumps audio itself.
`Loom.Audio.Synth`'s `OfflineSession` wraps one, keeping what it renders ([the synthesis guide](synth.md)).
`Loom.Tests` tests the mixer through it (`AudioEngineTests`, `AudioDuckTests`, `Audio3DTests`, `AudioStreamTests`, `AudioEventQueueTests`,
`WavDecoderTests`, `VorbisDecoderTests`, `FlacDecoderTests`), and the real
SDL device through `SdlAudioDeviceTests`, which skip where SDL3 or an output is missing.

---

## 8. Not built yet

- Decoders beyond WAV, Ogg Vorbis and FLAC (Opus).
- 3D beyond stereo panning: HRTF (above and behind), occlusion, reverb zones, and sound cones.
- An exclusive-mode WASAPI path below shared mode's 10 ms on Windows.

[`Planning.md` §10](../Planning.md#10-the-audio-track) tracks these.
