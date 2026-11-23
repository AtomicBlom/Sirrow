# Sirrow — low-latency audio for .NET

`BinaryVibrance.Sirrow` is low-latency audio for .NET: a mixer with buses and
pooled voices, clips decoded into memory, and **sources** — sound a game writes itself, sample by sample, for software
synthesizers, procedural engine notes and generated music. It depends only on the BCL, and needs no engine. Each
platform's output device is a package of its own behind one seam, `IAudioDevice`:

| Platform | Package | Backend | Where the mix runs | Latency it adds |
|---|---|---|---|---|
| Desktop | `BinaryVibrance.Sirrow.Sdl` | SDL 3 | SDL's audio thread | one device buffer (the platform's period: 10 ms on Windows' shared-mode WASAPI) |
| Android | `BinaryVibrance.Sirrow.Android` | AAudio, low-latency, exclusive where the device allows | AAudio's callback thread | two bursts (4 ms on a Galaxy S23 Ultra's exclusive MMAP path) |
| Browser | `BinaryVibrance.Sirrow.Web` | an AudioWorklet | the frame thread, a 50 ms lead ahead of the speaker | the lead, by design: the .NET runtime can't run on the browser's audio thread |

This page is the summary. [The audio guide](../docs/audio.md) covers the
whole API, the threading model, and what each platform's device does.

## Use it

```csharp
// Each entry point registers its platform's output, once:
SdlAudio.Register();            // desktop
AndroidAudio.Register();        // Android
await WebAudio.RegisterAsync(); // browser

// Then, the same everywhere:
var options = new AudioOptions();
var audio = new AudioEngine(AudioOutput.Open(options), options);
var music = audio.CreateBus("music");
var sfx = audio.CreateBus("sfx");
var hit = audio.LoadWav(File.ReadAllBytes("sounds/hit.wav"));
audio.Play(synth, music);               // an AudioSource of the game's own
audio.Play(hit, sfx, volume: 0.8f, pitch: 1.1f, pan: -0.3f);
synth.Cutoff.Value = 1200f;             // an AudioParameter, read on the audio thread

// Once a frame, after the frame has started what it plays (in the browser, the frame's mix is sent here):
audio.Update();
// As the app leaves the screen and comes back, loses and regains focus, and closes:
audio.Pause(); audio.Resume(); audio.SetFocused(false); audio.Dispose();
```

`samples/HelloAudio` is a whole app: an eight-voice synthesizer played from the keyboard, an arpeggiator running on
the audio thread's sample clock, and a clip fired from the game, on the desktop, on Android and in the browser.

Effects for buses (`BinaryVibrance.Sirrow.Effects`), the DSP building blocks a source is made of
(`BinaryVibrance.Sirrow.Dsp`) and rendering offline (`OfflineSession`, `WavWriter`, `AudioAnalysis`) are in the same
package. A synthesizer chip with patches, drum kits and songs is [Lunelle](https://github.com/AtomicBlom/Lunelle).

## Write a source

```csharp
sealed class Sine : AudioSource
{
	public AudioParameter Frequency { get; } = new(440f);
	private float _phase, _rate;

	protected override void OnStart(int sampleRate) => _rate = sampleRate;   // game thread: size and precompute here

	protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
	{
		var frequency = Frequency.Ramp(info.Frames);                          // glides across the block: no clicks
		for (var i = 0; i < info.Frames; i++)
		{
			left[i] = right[i] = 0.2f * MathF.Sin(2 * MathF.PI * _phase);
			_phase = (_phase + frequency.Next() / _rate) % 1f;
		}
		return true;                                                          // false ends the voice
	}
}
```

`Render` runs on the audio thread against a deadline of one device buffer. It must not allocate, lock or block: a
garbage collection stops the audio thread along with every other managed thread, so an allocation there is the one
thing that can stall the mix. An exception thrown there stops the voice and is thrown again from the game thread.

Steer a source from the game with:
- **`AudioParameter`** — a continuous value (a cutoff, an RPM, an intensity). The game sets `Value`; the source takes
  a `Ramp` once a block. Only the latest value matters.
- **`SmoothedParameter`** — the same, eased with a time constant rather than ramped across one block, for a value the
  game moves in steps many times a second (an RPM set every physics step). `Begin()` once a block, `Next()` a sample.
- **`AudioEventQueue<T>`** — discrete events that must all arrive, in order (a note on, a note off). Lock-free, one
  thread posting and one taking.

A bus can run **`AudioEffect`**s (`AddEffect`: a reverb, a limiter), under the same rules as `Render`, before its
volume; and a **send** (`AddSend`) mixes a share of one bus into another, as a reverb bus is fed, in an order the engine derives from what feeds what.

## The threading model

The engine is called from the thread that made it (the app's frame thread); a call from any other throws, except
making clips (`CreateClip`, `LoadWav`), which a loading thread may do. Each call queues a command the audio thread
takes at the start of its next block, so a play is heard within one device buffer. Nothing is shared under a lock,
and neither side allocates once running (`AudioEngineTests.The_mix_allocates_nothing_once_running` holds it to that).
`AudioEngine.Stats` reports the mix's clock, its render load against the deadline, and its voices.

## What isn't here yet

- Decoders beyond WAV, Ogg Vorbis and FLAC (Opus).
- 3D beyond stereo panning: HRTF, occlusion, reverb zones, sound cones.
- A direct low-latency WASAPI path below the 10 ms shared-mode period on Windows.
