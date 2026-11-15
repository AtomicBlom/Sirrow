# Synthesis

What [`Loom.Audio.Synth`](../Loom.Audio.Synth) adds over [the mixer](audio.md): the DSP building blocks a source is
made of, and sound rendered offline, to measure it in a test or save it as a WAV file to listen to.

The library depends only on `Loom.Audio` and the BCL. The Loom SDK doesn't reference it for an app: an app or test
project that wants it references it itself.

---

## 1. Rendering offline: `OfflineSession`

An `OfflineSession` is an `AudioEngine` over an `OfflineAudioDevice` that keeps what it renders. Nothing renders
until it is asked, and then as fast as the CPU goes, on the calling thread.

```csharp
using var session = new OfflineSession(sampleRate: 48_000);
var music = session.Engine.CreateBus("music");
session.Engine.Play(new MySynth(), music);

session.RenderSeconds(2.0);
float[] left = session.Left();
session.SaveWav("out/synth.wav");
```

- It renders in blocks of at most 256 frames and calls `Engine.Update()` after each, as a host calls it once a frame.
  So a source that finishes is freed in the block it finished in, and an exception a source throws comes out of
  `Render` at the block it was thrown in.
- `Samples` is everything rendered since the session began or since `Clear()`, interleaved stereo. `Clear()` forgets
  it, and the engine plays on from where it was.
- Every call comes from the thread that made the session, as with any `AudioEngine`.
- Two sessions set up the same way render the same bits, so a test may compare renders exactly.

## 2. WAV files: `WavWriter`

`WavWriter.Write` writes samples as a WAV file of 32-bit float PCM, to a stream or a path (making the path's folder
if it is missing). Every editor opens it, and so does `AudioEngine.LoadWav`.

```csharp
WavWriter.Write("out/tone.wav", samples, channels: 1, sampleRate: 48_000);
```

- Mono or stereo; stereo samples are interleaved.
- Floats aren't clipped or dithered: what was rendered is what is written, beyond -1..1 included.

## 3. Measuring: `AudioAnalysis`

Measures of a rendered signal, for tests and reports:

| Call | What it measures |
|---|---|
| `Peak(samples)` | The largest magnitude |
| `Rms(samples)` | The root of the mean square |
| `ToDb(amplitude)`, `FromDb(db)` | Amplitude to decibels (20·log10) and back |
| `LevelAt(mono, hz, rate)` | The amplitude of the sinusoid at one frequency, through a Hann window over the whole span |
| `Spectrum(mono)` | The amplitude of each bin, 0 to N/2, over the first N samples (the largest power of two that fits) |
| `PeakFrequency(mono, rate, minHz, maxHz)` | The strongest frequency in a range, refined between bins |

A sine of amplitude A reads A from `LevelAt` at its frequency, and from `Spectrum` when it sits on a bin's centre.

```csharp
var left = session.Left();
AudioAnalysis.PeakFrequency(left, session.SampleRate, 100, 2000).ShouldBe(440, 0.5);
AudioAnalysis.ToDb(AudioAnalysis.Rms(left)).ShouldBeLessThan(-3f);
```

The rules:

- They allocate (the spectrum's arrays) and run in `double`: they are for tests and tools, never the audio thread.
- A frequency between two bins of `Spectrum` reads lower than its amplitude, by up to 1.4 dB with the Hann window.
  `LevelAt` reads any frequency exactly, so measure a known frequency with it.

## 4. Building blocks: `Loom.Audio.Synth.Dsp`

What an `AudioSource` is made of. The structs hold their own state and are meant to be **fields of a source**: set
them up in `OnStart` (on the game thread), step them in `Render`. None allocates after it is made, so they are safe
on the audio thread. Per-sample methods are inlined.

| Type | What it is |
|---|---|
| `Units` | Notes and hertz, cents and ratios, decibels and gains |
| `SineTable` | A sine read from a 4096-step table, within 1e-5 of the true one; phase in cycles, any value |
| `PolyBlep` | Band-limited sawtooth and pulse waves: `Saw(phase, dt)`, `Square(phase, dt, width)` |
| `Noise` | White noise from a seed (xorshift), the same on every platform |
| `PinkFilter` | Turns white noise pink, the same energy in every octave |
| `OnePole` | A 6 dB-an-octave low or high pass, cheap enough to set every block |
| `Svf` | A 12 dB-an-octave state-variable filter (low, band, high), stable while its cutoff moves; the band pass is unity at its centre |
| `Biquad` | Cookbook filters for equalisation: low and high pass, peak, shelves. Set rarely |
| `DelayLine` | A delay read at whole or fractional (Hermite) delays; read, then write, each sample. A class: make it in `OnStart` |
| `EnvelopeShape`, `Envelope` | Attack, decay, sustain, release: a straight attack, exponential falls of 60 dB over their times. `Struck(seconds)`, `Gate` |
| `Lfo` | A slow sine to move something else |
| `Resonator` | One ringing mode: an impulse rings at about unit amplitude and dies by 60 dB over its decay |
| `Oversampler` | 2× or 4× up and back down by half-band filters, so a waveshaper between doesn't alias. A class |
| `Denormal` | Flushes a filter's memory to zero before it reaches the slow denormal range |

Call each filter's `Flush()` once a block: a filter fed silence decays toward the denormal range, where a float is
many times slower to work with.

```csharp
sealed class Buzz : AudioSource
{
	private Svf _filter;
	private float _phase;
	private int _rate;

	public SmoothedParameter Cutoff { get; } = new(800f, seconds: 0.02f);

	protected override void OnStart(int sampleRate)
	{
		_rate = sampleRate;
		Cutoff.Prepare(sampleRate);
	}

	protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
	{
		Cutoff.Begin();
		_filter.Set(Cutoff.Skip(info.Frames), 2f, _rate);
		var dt = 110f / _rate;
		for (var n = 0; n < info.Frames; n++)
		{
			var x = _filter.LowPass(PolyBlep.Saw(_phase, dt)) * 0.3f;
			_phase += dt;
			_phase -= MathF.Floor(_phase);
			left[n] = right[n] = x;
		}
		_filter.Flush();
		return true;
	}
}
```
