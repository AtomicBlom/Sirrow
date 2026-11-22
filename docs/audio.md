# Audio

How to make sound in a Loom app: clips played on voices, mixed through buses, and **sources**, sound the game writes
itself sample by sample (software synthesizers, procedural engine notes, generated music). Then what runs on which
thread, and what each platform does underneath.

[`Loom.Audio`](../Loom.Audio) holds the mixer and depends only on the BCL, as `Loom.Input` does. It needs no engine:
the engine knows nothing of audio, and an app opens its output itself. Each platform's output is a package of its own
behind one seam, `IAudioDevice`:

| Platform | Package | Device | Where the mix runs | Latency the mix adds |
|---|---|---|---|---|
| Desktop | [`Loom.Audio.Sdl`](../Loom.Audio.Sdl) | SDL 3, the system's default output | SDL's audio thread | one device buffer: 480 frames (10 ms) on Windows, the floor of shared-mode WASAPI |
| Android | [`Loom.Audio.Android`](../Loom.Audio.Android) | AAudio, low-latency, exclusive where the device allows | AAudio's callback thread | two bursts: 192 frames (4 ms) on a Galaxy S23 Ultra's exclusive MMAP path |
| Browser | [`Loom.Audio.Web`](../Loom.Audio.Web) | an AudioWorklet | the frame thread, 50 ms ahead of the speaker | the 50 ms lead, by design (see [The browser](#the-browser)) |

`samples/HelloAudio` is a whole app built on all of it: an eight-voice synthesizer played from the keyboard, an
arpeggiator timed on the audio clock, and a clip fired from the game. It builds as a desktop exe, an Android APK and a
browser app from the same source.

Sound built on top of the mixer (rendering offline to measure it or save it as a WAV file) is in
[`Loom.Audio.Synth`](../Loom.Audio.Synth), and [the synthesis guide](synth.md) covers it.

---

## 1. Turning it on

Each of the app's entry points registers its platform's output, once, before anything opens audio:

```csharp
SdlAudio.Register();            // the desktop's Main (Loom.Audio.Sdl)
AndroidAudio.Register();        // the Android activity (Loom.Audio.Android)
await WebAudio.RegisterAsync(); // the browser's Main (Loom.Audio.Web): loads its page module
```

Shared code then opens the output the same way everywhere, makes an `AudioEngine` over it, and plays. The rest of this
guide calls the engine `Audio`, as a property of the app's:

```csharp
var options = new AudioOptions(); // the device's own rate and its smallest buffer
Audio = new AudioEngine(AudioOutput.Open(options), options);
var sfx = Audio.CreateBus("sfx");
var hit = Audio.LoadWav(File.ReadAllBytes("sounds/hit.wav"));
Audio.Play(hit, sfx, volume: 0.8f, pitch: 1.1f, pan: -0.3f);
```

`AudioOutput.Open` tries the registered outputs in the order they were registered. Where none opens (SDL3.dll missing,
no output), it returns a `NullAudioDevice` that says why, and the app runs on, silent. Registering is explicit, not
done by a package on its own as it loads: an assembly nothing names may never load, and NativeAOT and the browser's AOT
keep only what is reached.

The app runs the engine through its own lifecycle. These are the calls, and when to make them:

| When | Call |
|---|---|
| As the app loads | `new AudioEngine(AudioOutput.Open(options), options)`, which starts the device. `Audio.Device` says what was granted: the backend, the output, the rate and the buffer. |
| Once a frame, after the frame has started what it plays | `Audio.Update()`: frees the voices that ended, throws what a source threw on the audio thread, and pumps the device. In the browser, that is when the frame's mix is rendered and sent, so an app that skips it plays nothing there. |
| As the app leaves the screen, and comes back | `Audio.Pause()` and `Audio.Resume()`. Voices hold where they are. |
| As the window or tab loses or regains focus | `Audio.SetFocused(focused)`: the output fades to `AudioOptions.UnfocusedVolume` and back (see below). |
| As the app closes | `Audio.Dispose()`, which closes the device. |

In a `LoomApplication`, those are `LoadContent`, `BeforeRender`, `OnSuspending` / `OnResumed`, `OnFocusChanged` and
`UnloadContent`. `samples/HelloAudio` does exactly that.

### `AudioOptions`

Every value is a request the platform meets as closely as it can. `Audio.Device` says what was granted.

| Option | Default | Meaning |
|---|---|---|
| `SampleRate` | 0 (the device's own) | The mixing rate. Any rate but the device's own makes the platform resample, which adds latency. |
| `BufferFrames` | 0 (the smallest the backend offers) | The device buffer, which is the latency the mixer adds. Raise it only if the output crackles on a slow device. In the browser it is the lead ahead of the speaker. |
| `MaxVoices` | 64 | Voices playing at once, clips and sources together. A play past it is dropped (see [Voices](#3-voices)). |
| `MaxBuses` | 16 | Buses, the master bus included. |
| `MaxDucks` | 8 | Ducks (see [Ducking](#ducking)). |
| `MaxEffectsPerBus` | 8 | Effects on one bus (see [Effects and sends](#effects-and-sends)). |
| `MaxSends` | 16 | Sends between buses. |
| `SpeedOfSound` | 343 | The speed of sound in the game's units a second, for the Doppler shift of 3D voices: 343 for metres, 1125 for feet. |
| `UnfocusedVolume` | 1 (no change) | The output's volume while the app is out of focus: another window in front, the browser tab hidden, the app behind another on Android. The whole mix fades to it over a quarter second and back on return. 0 silences an app in the background. It is a gain of its own after the master bus, so the game's volumes, the master's included, are left alone, and it multiplies with them. `Audio.SetUnfocusedVolume` changes it while the app runs, for a setting the player chooses. |
| `CommandCapacity` | 4096 | Commands (plays, stops, volume changes) waiting for the audio thread at once. Past it the engine throws rather than drop one. |
| `Work` | `Thread` | Where the work off the audio thread runs (decoding streams, rendering [ahead sources](#ahead-of-the-speaker-aheadsource)): `AudioWork.Thread`, a worker thread of the engine's, or `AudioWork.Update`, in `Audio.Update()` on the frame thread, which is deterministic (tests and offline renders; `OfflineSession` takes it by default). In the browser `Thread` is taken as `Update` unless `BrowserThread`. |
| `BrowserThread` | false | In the browser, give the work its own thread as on the desktop, rather than the frame thread. A thread made while a browser app runs can hang its frames, so set `StartWorker` with it. |
| `StartWorker` | false | Make the worker as the engine is made, rather than when the first stream or ahead source needs it. |

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
- Code that keeps samples itself (a synthesizer's sample player, a tool) decodes without an engine:
  `AudioDecoder.DecodeWav`, `DecodeOgg`, `DecodeFlac`, or `Decode` (any of them, by the file's first four bytes) give an
  `AudioData` at the file's own rate, with the same exceptions.
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
- **Decoding never happens on the audio thread.** The engine's worker thread decodes about half a second ahead into a
  lock-free ring, waking every 10 ms, and the audio thread reads the ring. With `AudioWork.Update` (and in the browser,
  where the mix runs on the frame thread, by default), `Audio.Update()` decodes ahead instead. A play decodes the first 100 ms on the calling
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

### Effects and sends

An effect changes the sound passing through a bus: a reverb, a limiter, a distortion. Subclass `AudioEffect`, write
`Process`, and add it to a bus. A send mixes a share of one bus into another as well as into its parent, which is how
several buses share one reverb.

```csharp
var sfx = Audio.CreateBus("sfx");
var space = Audio.CreateBus("space");          // the room several buses send to
Audio.AddEffect(space, new Reverb());          // Loom.Audio.Synth.Effects; Wet 1, Dry 0 suit a bus fed by sends
var toSpace = Audio.AddSend(sfx, space, 0.1f);
Audio.AddEffect(Audio.Master, new Limiter());

Audio.SetSendLevel(toSpace, 0.4f, fadeSeconds: 0.2f); // more of the sound off the walls
```

- **The order within a bus is its effects, then its volume, then its ducks.** Effects run on everything mixed into
  the bus, before its volume, so a reverb's tail is turned down by the bus's volume with everything else. They run in
  the order added. On the master bus they run before the focus volume and the final clip.
- **Buses and sends are made in any order.** The engine works out the order to mix them in from what feeds what: a bus
  is finished before the bus it is mixed into and before the buses it sends to, so each receives the whole of what is
  sent to it. A send that would make a loop (a bus sending to one mixed into it, or round through other sends), a send
  to itself or to a bus it is already mixed into, and a send from the master throw. The sound sent is the bus's after
  its effects, volume and ducks.
- `Process` runs on the audio thread under the rules for a source's `Render` ([The rules for `Render`](#the-rules-for-render)):
  size buffers in `OnPrepare`, which `AddEffect` calls on the game thread. Its controls are `AudioParameter` and
  `SmoothedParameter` members of its own.
- An effect instance belongs to one bus: adding it to a second throws. `AudioEffect.Prepare`, `Run` and `Process` are
  public so a synthesizer can run effects on groups of its own voices.
- **An effect costs nothing while nothing plays through it.** The mixer calls `Run`, which calls `Process` unless
  silence (120 dB under full scale) has gone in and come out for the effect's `MemorySeconds`: then the effect rests,
  the block is left alone, and it starts again with the first sound. A reverb's tail and a delay's echoes are heard out
  first. `MemorySeconds` is 1 by default; override it in an effect with a longer delay (`Delay` gives its longest time),
  or return infinity from one that makes sound of its own, which never rests. `IsResting` says whether it is. A song
  whose distorted guitar plays in one section pays for the distortion only there: two test songs rendered twice as
  fast, the same sample for sample.
- An effect that throws is skipped from then on, the sound passing unchanged, and the exception is thrown from the next
  `Audio.Update`, as a source's is.
- Add effects and sends at load. A bus holds at most `AudioOptions.MaxEffectsPerBus`, the engine at most
  `AudioOptions.MaxSends`, and none is removed: `Bypass` turns an effect off, and a level of 0 silences a send.
- `Loom.Audio.Synth`'s `Effects` namespace has a reverb, delay, chorus, compressor, limiter, EQ and distortion
  (`docs/synth.md`, "Effects").

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

The game and the audio thread share no lock, so a source takes its controls through these lock-free types:

| Type | For | Game thread | Audio thread |
|---|---|---|---|
| `AudioParameter` | A continuous value where only the latest matters, reached within the block: a cutoff, a level set from a menu. | Set `Value` whenever it likes. | `Ramp(info.Frames)` once a block, then `Next()` once a sample: a straight line from where it was to `Value`. `Snap()` jumps straight there, for a control where a step is what's wanted (a waveform choice). |
| `SmoothedParameter` | A continuous value the game moves often and in steps, eased with a time constant (`seconds`, after which it has gone 63% of the way): a motor's revs, a wind's strength, set every physics step. An `AudioParameter` would follow it as a staircase of short ramps. | Set `Value` whenever it likes. | `Prepare(rate)` in `OnStart`; `Begin()` once a block, then `Next()` once a sample, or `Skip(frames)` for a value read once a block. `Snap()` jumps straight there. |
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

### Ahead of the speaker: `AheadSource`

A source that costs a lot to render, and can answer its controls a little late, can be rendered ahead of the speaker,
off the audio thread:

```csharp
_music = new AheadSource(Audio, songSource, leadSeconds: 0.1);
Audio.Play(_music, musicBus);
```

- **The engine's worker renders the inner source** in blocks of 256 frames into a lock-free ring, keeping the lead
  waiting and waking every 2 ms while there are ahead sources (with `AudioWork.Update`, `Audio.Update()` does it). The
  audio thread only copies from the ring, so its cost there is a copy, whatever the inner source costs.
- **The lead is latency.** A change to the inner source's controls is heard up to the lead later. Music can take that
  (the music player's `aheadSeconds`, [synth.md](synth.md)); a sound effect, a sound that follows the player's hands or a click
  of the UI should play directly.
- **A play doesn't stall the frame.** With a worker, the worker renders the first lead and the source is silent until
  it is waiting (a few milliseconds), which isn't counted as an underrun; rendering it on the frame thread cost a song of
  nine tracks 9 ms there. With `AudioWork.Update` (tests, offline renders) the play renders the lead at once, so it
  sounds from its first block and a render is the same however it is run.
- **If the work falls behind,** the block plays silence where nothing waits, counted in `Underruns`, and the source
  carries on from where it stopped, none of it lost. `FramesHeard` and `FramesRendered` say how far each side has got.
- **The inner source must not play on its own** while wrapped (a play of it throws), and its controls keep one reader:
  the thread rendering it ahead, in place of the audio thread. What the inner source throws is thrown again from
  `Audio.Update()`, as a stream's fault is. `Dispose` stops it and takes it out of the worker's list.

`AheadSourceTests` holds it to that: frames in order with none lost, the lead kept, an underrun counted and recovered
from, the end and a fault passed on, nothing allocated once it plays, and a real-time device kept up with by the worker.

---

## 6. Threads

Three threads share an `AudioEngine`:

- **The frame thread** (the thread that made the engine, the app's in a `LoomApplication`) calls every method. A call
  from any other thread throws, except making clips (`CreateClip`, `LoadWav`), which touches no shared state.
- **The audio thread** (the device's) mixes. It never waits on the game.
- **The worker** (`Loom audio worker`), when streams or ahead sources need it (or at once, with `StartWorker`):
  decodes streams ahead and renders ahead sources ahead. It never touches the mix; with `AudioWork.Update` there is
  none, and `Update` does its work.

| Thread | Runs | Wakes |
|---|---|---|
| Frame | every engine call; `Update` | each frame |
| Audio | the mix: sources' `Render`, effects, buses | each device buffer (10 ms on Windows) |
| Worker | streams' decoding, ahead sources' rendering | every 2 ms with ahead sources, 10 ms without |

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
| `RenderLoad`, `MaxRenderLoad` | How long the last render took, as a fraction of the time its samples last (0.1 is 10% of the deadline), and the highest since the first quarter second (or since `Audio.ResetMaxRenderLoad()`, which a display showing each second's peak calls once a frame or once a second). Near 1, the output drops out. |
| `DeviceUnderruns` | How many times the output ran out of samples since it opened, as the platform counts them (`IAudioDevice.Underruns`): each is a click or a gap. AAudio counts them (its xruns, carried across a reopen); SDL and the browser don't, and report -1. |

On Android the load reads high for the work done: AAudio runs its callback on a thread the scheduler keeps on a slow,
clocked-down core until the load asks for more, against a deadline of a burst or two (2 ms at 96 frames). A mix that
costs 2% of a core measured offline can read 30% there, with peaks past 50%, and still not drop out: on a Galaxy S23
Ultra, three minutes of a game's busiest play at 30% (peaks to 78%) added no underruns to the ten its start made, while the mix was
compiling and the app loading on every core. The underruns, not the load, say whether it drops out.

---

## 7. Platforms

### The desktop

`Loom.Audio.Sdl` opens the system's default output through SDL 3, at the device's own rate in 32-bit float stereo, and
asks for a 128-frame buffer. SDL rounds it to what the platform's audio API takes: on Windows that is shared-mode
WASAPI's period, 480 frames (10 ms) at 48 kHz. An output on the default device follows the system's default as it
changes (headphones plugged in), which SDL does on its own.

SDL 3 comes from the `BinaryVibrance.Kaldra.SDL3` package, the same `SDL3.dll` the desktop host reads gamepads with
(see [api-vocabulary.md](api-vocabulary.md#input-on-the-desktop-loomhostingdesktop)), so an app with both loads one
SDL. Each starts and stops only its own SDL subsystem, so closing one never stops the other. Where SDL3 is missing or
won't load, `AudioOutput.Open` returns a `NullAudioDevice` and the app runs silent.

### Android

`Loom.Audio.Android` opens an AAudio output stream: low-latency performance mode, exclusive sharing where the device
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
  functions in `libloomaaudio.so` (`Loom.Audio.Android/Native/loom_aaudio.c`), built without CFI, and they call the
  C# callbacks. The built libraries for `arm64-v8a` and `x86_64`, linked for 16 KB pages, are checked in under
  `Native/lib/`, so building needs no NDK. Rebuild them with `pwsh Loom.Audio.Android/Native/build.ps1` after
  changing `loom_aaudio.c`.

### The browser

The .NET runtime can't run on the browser's audio thread, so `Loom.Audio.Web` turns the usual arrangement around. The
mix runs on the frame thread, once a frame, in `AudioEngine.Update`. It tops up an AudioWorklet's queue to a lead of 50 ms
ahead of the speaker (`AudioOptions.BufferFrames` sets it), and the worklet plays from that queue on the browser's
audio thread. The lead is the web's latency, and it is what rides out a late frame: a frame later than the lead is
heard as a gap.

Because the mix runs on the frame thread, a source's `Render` runs there too. The rules for `Render` and the lock-free
types work the same, so a source written for the desktop runs unchanged.

- **A page makes sound only after the player's first click, tap or key.** `loom-audio.js` listens for it and resumes
  the AudioContext. Until then the mix keeps time (voices advance and end) and sends nothing.
- When the frame loop stalls (a background tab), the mix skips the time it missed, a quarter second at most, rather
  than rendering the whole gap at once.
- `__loomAudio()` at the browser's console reports the output's state, its rate and latencies, and the frames sent and
  played (sent minus played is how far the mix runs ahead of the speaker).
- `WebAudio.RegisterAsync` loads the `loom-audio` JS module itself (from `_content/Loom.Audio.Web/`), so the page needs
  nothing added, whoever boots the runtime.

### An output of your own, and tests

An output is an `IAudioDevice`, registered with `AudioOutput.Register(name, open)`, where `open` returns the device
opened for the options or null with the reason it couldn't. Once started, a device asks the renderer for interleaved
stereo 32-bit float at its `SampleRate`, on a thread of its own, or in `Pump`, which `AudioEngine.Update` calls once a
frame (the browser's does that), and it pauses and resumes on request.

An `AudioEngine` also works on its own, over any `IAudioDevice`. `OfflineAudioDevice` renders only when asked, on the
caller's thread: for tests, for rendering to a file faster than real time, and for a host that pumps audio itself.
`Loom.Audio.Synth`'s `OfflineSession` wraps one, keeping what it renders ([the synthesis guide](synth.md)).
`Loom.Tests` tests the mixer through it (`AudioEngineTests`, `AudioEffectTests`, `AudioDuckTests`, `Audio3DTests`, `AudioStreamTests`, `AudioEventQueueTests`,
`WavDecoderTests`, `VorbisDecoderTests`, `FlacDecoderTests`), opening an output and pumping it through
`AudioOutputTests`, and the real SDL device through `SdlAudioDeviceTests`, which skip where SDL3 or an output is
missing.

---

## 8. Not built yet

- Decoders beyond WAV, Ogg Vorbis and FLAC (Opus).
- 3D beyond stereo panning: HRTF (above and behind), occlusion, reverb zones, and sound cones.
- An exclusive-mode WASAPI path below shared mode's 10 ms on Windows.

[`Planning.md` §10](../Planning.md#10-the-audio-track) tracks these.
