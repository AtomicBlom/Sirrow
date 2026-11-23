# Effects and offline rendering

What `BinaryVibrance.Sirrow` has beside [the mixer](audio.md): rendering offline, to measure sound in a test or save
it as a WAV file to listen to; the DSP building blocks a source is made of; and effects for buses.
[Lunelle](https://github.com/AtomicBlom/Lunelle), a synthesizer chip with patches, drum kits and songs, is built on
them.

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

## 4. Building blocks: `BinaryVibrance.Sirrow.Dsp`

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
| `EnvelopeShape`, `Envelope` | Attack, decay, sustain, release: a straight attack, exponential falls of 60 dB over their times. `Struck(seconds)`, `Gate`. `Next()` a sample, or `Fill(span)` a block at a time |
| `Lfo` | A slow sine to move something else |
| `Resonator` | One ringing mode: an impulse rings at about unit amplitude and dies by 60 dB over its decay |
| `Oversampler` | 2× or 4× up and back down by half-band filters, so a waveshaper between doesn't alias. Delays by a whole `LatencyFrames` (31 or 47), so a dry copy delayed as much lines up. Each step skips the filter's zero taps and the raised signal's zero samples, a quarter of the full filter's work. A class |
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

## 5. Effects: `BinaryVibrance.Sirrow.Effects`

`AudioEffect`s to put on a bus with `AudioEngine.AddEffect` ([Effects and sends](audio.md#effects-and-sends)), or to
run on a synthesizer's own groups. Each is a sealed class with its controls as public `AudioParameter` and
`SmoothedParameter` properties: set their `Value` from the game at any time. A control marked *eased* glides to a new
value; the rest are read once a block. Each allocates only when it is made and prepared.

What makes its own instances (Lunelle's chip groups and songs) takes effects as factories: `Fx.Reverb(decaySeconds,
wet, dry)`, `Fx.Delay(seconds, feedback, pingPong, wet, dry)`, `Fx.Chorus(mix)`, `Fx.Compressor(…)`, `Fx.Duck(…)`,
`Fx.Eq(lowDb, highDb)` and `Fx.Distortion(driveDb, toneHz, curve, cabinet, outputDb, oversampling)`, each with the
defaults of the tables below.

A reverb shared by several buses, through sends:

```csharp
var sfx = Audio.CreateBus("sfx");
var music = Audio.CreateBus("music");
var space = Audio.CreateBus("space");     // the room both send to: buses may be made in any order
var reverb = new Reverb(size: 1.2f);
Audio.AddEffect(space, reverb);
var sfxToSpace = Audio.AddSend(sfx, space, 0.1f);
Audio.AddSend(music, space, 0.05f);
Audio.AddEffect(Audio.Master, new Limiter());

// In a cave: more of the effects off the walls, and a longer tail
Audio.SetSendLevel(sfxToSpace, 0.4f, fadeSeconds: 0.2f);
reverb.DecaySeconds.Value = 2.5f;
```

### `Reverb`

A room: eight delay lines feeding back through a mixing matrix, behind four all-passes that smear the input, each line
losing highs as it goes round. Mono in, stereo out. `new Reverb(size)` scales the room's lines, 0.5 to 2. Its
defaults (all wet, no dry) suit a bus fed only by sends.

**Four of the lines sway.** Their lengths move by up to ten samples either way, each at its own rate under once a
second: about two cents, too little and too slow to hear as a wobble. A network held still rings a held note's tail on
at one or two frequencies of its own near the note; swaying, the tail spreads over the frequencies round it. Measured
on a second of one note into a six-second room, the strongest line in the tail stood 20 to 30 times over the average
near it held still, and 6 to 15 times at full sway (at 440 Hz, 1 kHz and 2.5 kHz; the measure wanders from note to
note). `Modulation` at 0 holds it still, which also makes the tail the same every time.

**The mix is a Hadamard's:** each time round, every line is fed from all eight in equal measure. That is what lets
four swaying lines move the whole tail. Fed mostly from itself (a Householder mix), a still line keeps its own
frequencies whatever the others do, and the same sway did little. The mix alone changed nothing measured: how even a
click's tail is from the start, how much it repeats itself a line's length later, and its level under steady noise
were the same either way, since the all-passes on the way in have already spread a click out before the lines come
round.

| Control | Default | Meaning |
|---|---|---|
| `DecaySeconds` | 1.8, eased over 0.1 s | How long the tail takes to fall 60 dB, 0.1 to 20 |
| `DampingHz` | 6000 | Where the tail loses its highs: lower is a softer room |
| `PreDelaySeconds` | 0.015 | The gap before the room answers, 0 to 0.25 |
| `Modulation` | 1 | How far four of the lines sway in length, 0 (still) to 1 |
| `Wet` | 1, eased | The reverb's share |
| `Dry` | 0, eased | The input's share |

### `Delay`

Echoes, each quieter and darker. `new Delay(pingPong, maxSeconds)`: ping-pong bounces the repeats between the
sides. `Delay.Beats(beats, bpm)` gives a time in step with music. A change of time glides, bending the echoes' pitch.

| Control | Default | Meaning |
|---|---|---|
| `TimeSeconds` | 0.375, eased over 0.2 s | The time to the first repeat, up to `maxSeconds` |
| `Feedback` | 0.35 | How much of each repeat comes round again, 0 to 0.95 |
| `DampingHz` | 4000 | A one-pole low-pass on the feedback: a click comes back smeared, a tone at its level |
| `Wet` | 0.5, eased | The repeats' share |
| `Dry` | 1, eased | The input's share |

### `Chorus`

Two copies a side, each delayed by about 12 ms swaying out of step with the others, so the sound thickens and widens.

| Control | Default | Meaning |
|---|---|---|
| `RateHz` | 0.6 | How fast the copies sway |
| `DepthSeconds` | 0.003 | How far each copy's delay sways either side of 12 ms, up to 0.01 |
| `Mix` | 0.5, eased | 0 is the input alone; 1 is the copies with half the input |

### `Compressor`

Brings a level over the threshold down by the ratio, both sides together by the louder. The amount the curve asks for
is held through the troughs between a wave's peaks by the release, then eased toward by the attack (a decoupled peak
detector), so a steady tone over the threshold settles where the ratio puts it. `GainReductionDb` reads how far down it
is, once a block, from any thread.

| Control | Default | Meaning |
|---|---|---|
| `ThresholdDb` | −18 | The level it starts at, in decibels of full scale |
| `Ratio` | 4 | Decibels in over the threshold for each one out |
| `KneeDb` | 6 | The width of the soft bend round the threshold; 0 is sharp |
| `AttackSeconds` | 0.01 | How fast it comes down (a time constant) |
| `ReleaseSeconds` | 0.15 | How fast it lets go |
| `MakeupDb` | 0 | Gain after it |

`Fx.Compressor(thresholdDb, ratio, attackSeconds, releaseSeconds, makeupDb, kneeDb)` makes one for a group's list. On a
group of drums, an attack of a few milliseconds lets each hit's front through before it comes down, which is punch, and
a release shorter than the gap between hits lets go before the next.

**Keyed** (`new Compressor(keyed: true)`), it listens to a key instead of to what it turns down: a sidechain. Whatever
runs it hands it the key through `IKeyedEffect.Key` before each block, even while the effect rests, so its envelope
never goes stale. `Fx.Duck(thresholdDb, ratio, attackSeconds, releaseSeconds)` makes one that ducks: on a bass's group,
with the kick as the key, the bass drops under each kick and comes back over the release, which is drum and bass's
pump. Lunelle's chip hands its key (what its patches send there) to the keyed effects on its groups, sends and master.
A keyed compressor where nothing hands it a key (on an engine's bus) throws from `Process` rather than quietly doing
nothing.

### `Limiter`

Keeps the output under a ceiling, for the end of a mix: it looks `LatencyFrames` (64) ahead, delaying the sound by as
much, so its gain is down before a peak arrives, and clamps whatever still gets past.

| Control | Default | Meaning |
|---|---|---|
| `CeilingDb` | −1 | The most the output reaches |
| `ReleaseSeconds` | 0.1 | How fast the gain comes back after a peak |

### `Eq`

A low shelf, two peaks and a high shelf, all flat until set. A band's filters are worked out again only in a block
where one of its values changed.

| Control | Default | Meaning |
|---|---|---|
| `LowHz`, `LowDb` | 120, 0 | The low shelf |
| `Peak1Hz`, `Peak1Q`, `Peak1Db` | 500, 1, 0 | The first peak: its centre, sharpness and gain |
| `Peak2Hz`, `Peak2Q`, `Peak2Db` | 3000, 1, 0 | The second peak |
| `HighHz`, `HighDb` | 8000, 0 | The high shelf |

### `Distortion`

Drives the sound into a curve, oversampled so its new harmonics don't alias; then a tone control and a speaker
cabinet's response. `new Distortion(curve, cabinet, oversampling)`: `DistortionCurve` is `Soft` (tanh), `Hard` (a
cubic into a hard clip), `Asymmetric` (tanh off centre, even harmonics) or `Fold` (folds back past ±1); `Cabinet` is
`None`, `OneByTwelve` or `FourByTwelve`; oversampling is 1, 2 or 4. The dry sound is delayed by the oversampler's own
`DryDelayFrames` so a mix lines up exactly. **Cost:** a stereo distortion at 4× with a cabinet is the dearest
effect, about 1% of a core; like every effect it rests while silence goes in and comes out ([Effects and sends](audio.md#effects-and-sends)), so on a song's group it costs that only while its tracks play. Nearly all of that is the 4× oversampler's
filters: at 1× (`oversampling: 1`, or `Fx.Distortion(…, oversampling: 1)`) it costs a fifth as much, and aliases.

| Control | Default | Meaning |
|---|---|---|
| `DriveDb` | 12, eased | How hard the sound is pushed into the curve |
| `ToneHz` | 4000 | A low-pass after the curve |
| `OutputDb` | −6, eased | The distorted sound's level |
| `Mix` | 1, eased | The distorted sound's share |

