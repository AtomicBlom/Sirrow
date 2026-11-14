# Loom.Audio — low-latency audio and software synthesis

`Loom.Audio` is the audio subsystem for the [Loom engine](https://github.com/AtomicBlom/Loom): a mixer with buses and
pooled voices, clips decoded into memory, and **sources** — sound a game writes itself, sample by sample, for software
synthesizers, procedural engine notes and generated music. It depends only on the BCL. Each platform's host opens the
output device behind one seam, `IAudioDevice`:

| Host | Backend | Where the mix runs | Latency it adds |
|---|---|---|---|
| Desktop (`Loom.Hosting.Desktop`) | SDL 3 | SDL's audio thread | one device buffer (the platform's period: 10 ms on Windows' shared-mode WASAPI) |
| Android (`Loom.Hosting.Android`) | AAudio, low-latency, exclusive where the device allows | AAudio's callback thread | two bursts (4 ms on a Galaxy S23 Ultra's exclusive MMAP path) |
| Browser (`Loom.Hosting.Web`) | an AudioWorklet | the frame thread, a 50 ms lead ahead of the speaker | the lead, by design: the .NET runtime can't run on the browser's audio thread |

This page is the summary. [The audio guide](https://github.com/AtomicBlom/Loom/blob/master/docs/audio.md) covers the
whole API, the threading model, and what each platform's device does.

## Use it from a LoomApplication

```csharp
protected override void Configure(AppOptions options) => options.Audio = new AudioOptions();

protected override void LoadContent()
{
	var music = Audio.CreateBus("music");
	var sfx = Audio.CreateBus("sfx");
	_hit = Audio.LoadWav(ReadContent("sounds/hit.wav"));
	Audio.Play(_synth, music);              // an AudioSource of the game's own
}

protected override void Update(FrameTime time)
{
	if (Input.Pressed(Key.Space))
		Audio.Play(_hit, sfx, volume: 0.8f, pitch: 1.1f, pan: -0.3f);
	_synth.Cutoff.Value = 1200f;            // an AudioParameter, read on the audio thread
}
```

`samples/HelloAudio` is a whole app: an eight-voice synthesizer played from the keyboard, an arpeggiator running on
the audio thread's sample clock, and a clip fired from the game.

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
- **`AudioEventQueue<T>`** — discrete events that must all arrive, in order (a note on, a note off). Lock-free, one
  thread posting and one taking.

## The threading model

The engine is called from the thread that made it (the app's frame thread); a call from any other throws, except
making clips (`CreateClip`, `LoadWav`), which a loading thread may do. Each call queues a command the audio thread
takes at the start of its next block, so a play is heard within one device buffer. Nothing is shared under a lock,
and neither side allocates once running (`AudioEngineTests.The_mix_allocates_nothing_once_running` holds it to that).
`AudioEngine.Stats` reports the mix's clock, its render load against the deadline, and its voices.

## What isn't here yet

- Streaming playback (music decoded as it plays) and decoders beyond WAV.
- 3D positioning.
- A direct low-latency WASAPI path below the 10 ms shared-mode period on Windows.
