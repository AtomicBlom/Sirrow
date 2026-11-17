# Synthesis

What [`Loom.Audio.Synth`](../Loom.Audio.Synth) adds over [the mixer](audio.md): the DSP building blocks a source is
made of, effects for buses, a 16-operator synthesizer chip with patches, samples and drum kits, music written as code
and its sequencer, and sound rendered offline, to measure it in a test or save it as a WAV file to listen to. Beside
it, `Loom.Audio.Workbench` is a window to write the music in (section 10).

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

## 5. Effects: `Loom.Audio.Synth.Effects`

`AudioEffect`s to put on a bus with `AudioEngine.AddEffect` ([Effects and sends](audio.md#effects-and-sends)), or to
run on a synthesizer's own groups. Each is a sealed class with its controls as public `AudioParameter` and
`SmoothedParameter` properties: set their `Value` from the game at any time. A control marked *eased* glides to a new
value; the rest are read once a block. Each allocates only when it is made and prepared.

A chip's groups and a song take effects as factories, since each chip makes its own instances: `Fx.Reverb(decaySeconds,
wet, dry)`, `Fx.Delay(seconds, feedback, pingPong, wet, dry)`, `Fx.Chorus(mix)`, `Fx.Compressor(…)`, `Fx.Duck(…)`,
`Fx.Eq(lowDb, highDb)` and `Fx.Distortion(driveDb, toneHz, curve, cabinet, outputDb, oversampling)`, each with the
defaults of the tables below.

A reverb shared by several buses, through sends:

```csharp
var space = Audio.CreateBus("space");     // made first: it receives sends
var sfx = Audio.CreateBus("sfx");
var music = Audio.CreateBus("music");
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

**Keyed** (`new Compressor(keyed: true)`), it listens to the chip's key instead of to what it turns down: a sidechain.
Patches send to the key with `p.Send(ChipSend.Key, level)` (§8), which nobody hears; the chip hands the key to every
keyed effect (`IKeyedEffect`) on its groups, sends and master before each block, even while the effect rests, so its
envelope never goes stale. `Fx.Duck(thresholdDb, ratio, attackSeconds, releaseSeconds)` makes one that ducks: on the
bass's group, with the kick sending to the key, the bass drops under each kick and comes back over the release, which is
drum and bass's pump. The key is taken after its voices' gain and pan, so the threshold is set against the key as loud
as it plays. A keyed compressor anywhere no chip hands it a key (on an engine's bus) throws from `Process` rather than
quietly doing nothing.

```csharp
var bass = song.Group("bass", Fx.Duck(thresholdDb: -30f, ratio: 8f, releaseSeconds: 0.1f));
var drums = song.Group("drums", Fx.Compressor(thresholdDb: -24f, ratio: 4f, attackSeconds: 0.008f, releaseSeconds: 0.07f, makeupDb: 10f));
song.Track("kit", kit, drums);            // its kick's patch does p.Send(ChipSend.Key, 1f)
song.Track("sub", subPatch, bass);
```

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
effect, about 1% of a core; like every effect it rests while silence goes in and comes out (`docs/audio.md`, "Effects
and sends"), so on a song's group it costs that only while its tracks play.

| Control | Default | Meaning |
|---|---|---|
| `DriveDb` | 12, eased | How hard the sound is pushed into the curve |
| `ToneHz` | 4000 | A low-pass after the curve |
| `OutputDb` | −6, eased | The distorted sound's level |
| `Mix` | 1, eased | The distorted sound's share |

## 6. Patches: `Loom.Audio.Synth.Chip`

A `Patch` is an instrument as data: up to 16 operators, which of them are heard, and what the voice does after them.
It is immutable. Write one with `Patch.Build`, whose action adds operators to a `PatchBuilder` and routes them:

```csharp
var bell = Patch.Build("bell", p =>
{
	var modulator = p.Sine(3.5f, Env.Decay(0.4f));
	var carrier = p.Sine(1f, Env.Decay(1.5f)).ModulatedBy(modulator, index: 2f);
	p.Out(carrier, p.Sine(5f, Env.Decay(0.3f)).At(0.2f));
	p.Filter(Filter.LowPass(6000f, keyTrack: 0.5f));
	p.Send(ChipSend.Space, 0.3f);
});
```

Each operator method returns an `Op` to route: `ModulatedBy(op, index)`, `Feedback(radians)`, `DrivenBy(ops…)`,
`Detune(cents)`, `Fixed(hz)`, `Phase(cycles)` (where in its cycle a sine or a wave starts each note: several detuned
saws all starting at 0 start as one loud wave, a click, where each given its own start is apart from the first sample),
`KeyLevel(dbPerOctave)` and `KeyDecay(share)` (how it follows the keyboard, from middle C: quieter going up, and
ringing shorter, as struck and plucked things do), and `At(level, pan)` for `Out`.

**Operators heard apart make a patch wide.** `Out` takes each operator at a level and a pan (-1 left to 1 right, the
middle by default): two saws a few cents apart with one each side are a section, where both in the middle are one
player. A patch with any operator off the middle is two channels from its operators on, each through the filter and the
drive, and the voice's own pan (the patch's and the note's) leans the pair as a balance. One with none stays a single
channel placed by its pan, and costs what it did. An operator in the middle is as loud in either.

`Env.Adsr(a, d, s, r)`, `Env.Decay(seconds)` (struck: dying
60 dB over its time whether held or not) and `Env.Gate` are the envelopes; `Filter.LowPass`, `BandPass` and `HighPass`
the filters, with key tracking, velocity and an envelope of their own.

| Operator | What it makes |
|---|---|
| `Sine(ratio, envelope, level, velocity)` | A sine at `ratio` times the note, phase-modulated by any operators before it (FM), with feedback on itself |
| `Wave(waveform, ratio, …)` | A band-limited saw or pulse |
| `Noise(envelope, …, pink)` | White or pink noise |
| `Level(envelope, …)` | Its envelope as a level: a breath's pressure |
| `Sample(samples, …, oneShot)` | A recording from a `SampleSet`, pitched from its zone's root; one-shot plays to its end |
| `String(ratio, decaySeconds, damping, pick)` | A plucked string: a pluck (or its drivers) round a tuned delay line, ringing for `decaySeconds` |
| `Tube(ratio)` | A flute (the Synthesis Toolkit's): a jet driving a bore, blown by its drivers, a `Level` and a quiet `Noise` |
| `Modes(modes…)` | A struck or rung body: its drivers ring up to eight `Mode`s, each at a ratio of the note (`Mode.At`) or a fixed frequency (`Mode.Fixed`) for its own time |

After the operators, in order: the heard operators mixed, the filter, drive (a soft clip, 0 to 1), the gain (times the
velocity squared), pan, and the sends. A patch also sets vibrato (after a delay), a tremolo, a pitch drop at each
note's start (which bends every operator, a `Fixed` one too, so a kick's sine at 55 Hz falls to it), mono with glide,
and macros: eight per instrument, each moving a target (the cutoff, drive, gain, a send,
the vibrato's depth, an operator's level) from one value to another as the game turns it from 0 to 1. The pitch, the
filter's cutoff and the levels are worked out every 64 frames (1.3 ms), so a drum's falling pitch and a filter's sweep
move rather than step.

The builder's calls for these are `p.Filter(…)`, `p.Drive(amount)`, `p.Gain(db)`, `p.Pan(pan)`, `p.Send(send, level)`,
`p.Vibrato(rate, depth, delay)`, `p.Tremolo(rate, depth)`, `p.PitchDrop(semitones, seconds)`, `p.Mono(glideSeconds)` and
`p.Macro(macro, target, from, to, op)`. A patch hears at most `Patch.MaxOutputs` (32) operators through `Out`.

**A filter is swept by octaves** (`MacroTarget.FilterOctaves`): the cutoff moves from so many octaves at the macro's 0
to so many at its 1, which is even to the ear all the way. `FilterCutoff` multiplies the cutoff, so a macro turned
steadily does nearly all it will do in its first few steps: from 100 Hz to 6.4 kHz it is past 3 kHz at halfway.

**The routing rule: an operator is modulated or driven only by one made before it.** So the operators render in
order, each reading the blocks of those before. `Build` checks the rest too, and throws a `PatchException` naming the
patch: at most 16 operators; at least one heard; modulators and feedback on a `Sine` only, drivers on a `String`, a
`Tube` or `Modes` only; at most two strings and one tube a patch; a tube and modes driven; one to eight modes; a sample
with samples. It checks every number as well, so the voice never meets one it can't play: ratios, a filter's cutoff and
Q, a string's and a mode's decay above 0; levels, detune and feedback finite; times and sends 0 or more; velocity,
sustain, damping, drive and a tremolo's depth 0 to 1; a pick 0.02 to 0.5; a pan -1 to 1; a pitch drop over some time.
A `Patch` is made only by `Patch.Build`. A recording is checked where it enters a chip (a `SampleSet` or a `DrumKit`):
samples at a rate, and its loop inside them.

**A pluck is a shape, not noise.** A string with no drivers starts from one period of what a plucked string is: a
triangle with its peak at `pick` (a share of the string's length from the bridge, 0.02 to 0.5; a fifth by default),
which lacks the harmonics with a still point there, mixed toward its slope the harder the note is played (brighter),
with a little noise on top. Every pluck of a note is then as loud as the last; noise alone, over the few samples of a
high note's period, made one loud and the next thin by 7 dB.

**`Modes` is how bars, bells, skins and bodies are made.** Drive it with a knock (a `Sine` at a fixed frequency under
`Env.Decay` of a few milliseconds: higher is a harder mallet; give it a level near 0.1, since a mode rings up to many
times its knock) for a marimba (`Mode.At` 1, 4, 9.2), a free bar (1, 2.76, 5.40, 8.93) or a skin (1, 1.59, 2.14); or
drive it with a `String` and hear both, for the box an instrument is strung on (`Mode.Fixed` at the box's own
resonances, with short times and a tap of a hundredth or so: a mode driven without a pause builds up far more than one
struck once).

The tube is the touchiest. It must be blown at a sustain of 0.8 or more (a `Level` of 1 at an envelope's sustain of
0.8) to speak below E4; it eases its own pressure below G#4 so low notes stay in their register, and it compensates
its loop's filters' delay at each note, so it holds within 20 cents from G3 to C6.

## 7. Samples and kits

`SampleData` is a mono recording at its own rate (`SampleData.From(AudioDecoder.Decode(bytes))`). A `SampleZone` says
which notes and velocities it plays for and its root; a `SampleSet` picks among zones by note and velocity, and among
alternate takes of the same range in turn.

**A recording with a loop sustains.** It plays to the loop's end and then round it for as long as the note is held, so
a short recording of a bowed note, an organ pipe or a pad lasts as long as it is asked to; the operator's envelope
ends it once the note is let go (a one-shot plays through the loop to its end). `recording.Looped(loopStart, loopEnd,
crossfadeFrames)` makes the loop, with a crossfade written into its last frames so its end arrives where its start
sets off from: no click. `recording.LoopedAt(hz, startSeconds, lengthSeconds)` finds the loop a steady note wants from
its pitch: the whole number of its periods nearest the length asked for, with a 20 ms crossfade. Choose a loop a whole
number of periods long: across one that isn't, the crossfade's two halves partly cancel and the level dips for its
length. Both copy the samples and leave the recording as it was.

A `DrumKit` is pieces, each sampled in velocity layers of alternate takes, or synthesised from a patch played at note
60 (`DrumKit.FromPatches`). `Kit` names the common pieces (`Kick`, `Snare`, `HatClosed`, `Crash` and the rest) so a
song written for one kit plays on another; any other name is allowed. A hit picks the first layer up to its velocity
and the next take in turn. A piece in a choke group above 0 silences the group's other pieces over 30 ms when hit: a
closed hi-hat the open one.

`DrumKit.Load(read, "kits/rock/kit.json")` reads a manifest and every file it names through `read`, relative to the
manifest's folder, so a kit loads from disk, embedded resources or a package alike. `tools/kit-cut.cs` writes these:

```json
{
  "name": "rock",
  "source": "Big Rusty Drums, Karoryfer Samples, CC0-1.0",
  "pieces": [
    { "piece": "kick", "gainDb": 0, "pan": 0, "choke": 0,
      "layers": [ { "upTo": 0.35, "takes": ["kick_1a.ogg", "kick_1b.ogg"] },
                  { "upTo": 1.0,  "takes": ["kick_2a.ogg", "kick_2b.ogg"] } ] }
  ]
}
```

A missing file, a layer with no takes, or layers out of rising `upTo` order throw `InvalidDataException` naming the
manifest and the piece.
## 8. The chip: `SynthChip`

A `SynthChip` is an `AudioSource` that plays instruments on a pool of voices:

```csharp
var chip = new SynthChip(new ChipOptions
{
	Voices = 32,
	Groups = [new("main"), new("guitars", () => new Distortion(cabinet: Cabinet.FourByTwelve))],
});
var bell = chip.AddInstrument(bellPatch);
var guitar = chip.AddInstrument(guitarPatch, group: 1);
var drums = chip.AddKit(kit);
Audio.Play(chip, music);

var note = chip.NoteOn(bell, 72, velocity: 0.7f);
chip.NoteOff(note);
chip.Play(guitar, 52, 0.8f, seconds: 0.5f);
chip.Hit(drums, Kit.Snare, 0.9f);
chip.SetMacro(guitar, new Macro(1), 0.6f);
```

- **Groups** each run a chain of effects over their instruments, then into the chip's master. **The sends** are
  shared by every patch: `ChipSend.Space` (a reverb by default), `ChipSend.Echo` (a delay, all wet), and
  `ChipSend.Key`, which is never heard: it is what keyed effects listen to (§5, `Compressor`). The master's own
  effects run last. Effects are factories, since each chip makes its own instances.
- **Stealing.** Past its voices a note takes the voice released longest, or else the oldest, fading it out over 128
  samples first. `Stats.StolenVoices` counts them. A note let go (`NoteOff`, `AllNotesOff`) while it waits for its
  stolen voice to fade never starts. When every voice is already fading out for another note, a new one is lost, and
  `Stats.DroppedNotes` counts it: raise `Voices` if that climbs.
- **Notes played for a time** (`Play(…, seconds)`, or a song's notes) are let go at their frame, not at the start of the
  block they end in, so a note shorter than a block still sounds.
- **Mono** patches have one voice: a new note moves it, gliding, with its envelopes left alone.
- Each call checks its arguments on the game thread, then queues an event the chip takes at its next block, so a note
  is heard within a block. Nothing allocates once the chip plays. A note is a MIDI number and may be fractional;
  velocity is 0 to 1, and the level goes as its square. `Hit` on a piece the kit hasn't throws, as a `Beat` naming one
  does. `AllNotesOff` lets go of everything.
- **Limits** (`ChipOptions`): `Voices` (32), `MaxInstruments` (64), and `EventCapacity` (1024) events waiting for the
  next block, past which a call throws rather than drop one.

**The cost**, measured on a Snapdragon X laptop in Release, ten seconds rendered:

| Load | Times real time |
|---|---|
| 32 voices of 16 plain sines, all heard | 5.8 |
| 16 voices of a 4-operator FM patch with feedback and a filter | 17 to 21 |
| An empty chip (its reverb and echo only) | 137 |

Feedback and the filter are each about a fifth of a voice's cost: both are recursions, each sample waiting on the one
before.

## 9. Music: `Loom.Audio.Synth.Music`

Music written as code, played on a chip by a sequencer on the audio clock. A song is a method that builds new data each
time it is called, so `dotnet watch` can rebuild it and `MusicPlayer.Swap` play the new one without a break:

```csharp
static class Groove
{
	// What the game tells the song, 0 to 1, and a macro its guitar patch turns: made once, since a cue is known by its object
	public static readonly Cue Energy = new("energy");
	public static readonly Macro Drive = new(1);

	public static Song Build(DrumKit kit, Patch bass, Patch guitar)
	{
		var song = new Song("groove") { Tempo = 128, Scale = Scale.Minor(Note.E) };
		var guitars = song.Group("guitars", Fx.Distortion(24, cabinet: Cabinet.FourByTwelve));
		var drums = song.Track("drums", kit).Humanise(4, 0.1f);
		var low = song.Track("bass", bass).Octave(2);
		var rhythm = song.Track("rhythm", guitar, guitars).Octave(3);
		rhythm.Macro(Drive).Follow(Energy, 0.2f, 1f);

		var verse = song.Section("verse", 4, "i | VI | III | VII");
		verse.Play(drums, Beat.Eighths((Kit.Kick, "x... x.x."), (Kit.Snare, "..x. ..x."), (Kit.HatClosed, "xxxx xxxx")))
			.EndWith(Beat.Sixteenths((Kit.TomHigh, "xxxx .... .... ...."), (Kit.TomLow, ".... xxxx .... ...."), (Kit.Snare, ".... .... ..xX xxXX")));
		verse.Play(low, Line.Eighths("1 1 . 1 5, 1 . 1").OverChords());
		verse.Play(rhythm, Strum.Eighths("D.DU .UDU").Voicing(Voicing.Power)).When(Energy.Above(0.4f));

		var chorus = song.Section("chorus", 4, "VI VII | i | VI VII | i");
		chorus.Play(drums, Beat.Eighths((Kit.Kick, "x.x. x.x."), (Kit.Snare, "..X. ..X."), (Kit.Crash, "x... ....")));
		chorus.Play(low, Line.Eighths("1 - 1 - 1 1 5, 1").OverChords());
		song.Order(verse, verse, chorus);
		return song;
	}
}
```

The game passes the song its instruments (here as arguments, so the sample stands alone; a game's songs name its own
patches and kits directly) and sets the cue as it plays: `music.Set(Groove.Energy, 0.7f)`.

### Scales and chords

A `Scale` is a root and its steps: `Major`, `Minor`, `Dorian`, `Phrygian`, `Lydian`, `Mixolydian`, `HarmonicMinor`,
`PentatonicMajor`, `PentatonicMinor`, `Blues`, or `Scale.Of(root, name, steps…)`. Music is written in its degrees:
`Degree(1)` is the root, 8 the octave above, 0 the step below. A player plays a song in its own scale or another
(`Play(song, Scale.Major(Note.G))`), which moves every note and no drum hit.

A `Chord` is a Roman numeral, its case meaning nothing (the scale decides major or minor): `i`, `VI5`, `bVII`, `V7`,
`ivsus4`. Flats and sharps go first; the suffixes are `M`, `m`, `5`, `sus2`, `sus4`, `7`, `dim`. With none, the scale's
own triad on that degree, diminished included. A section's chords are bars separated by `|`, each one chord, or two or
four sharing the bar: `"i | VI | III VII | i"`.

### The notations

A mistake in any of them throws a `NotationException` when the part is placed, quoting the text and saying where:
`Line "1 2 3 4 5 6 7": bar 1 has 7 steps; a bar of 4/4 in eighths has 8 (column 13)`. Every bar must be whole.

**`Line`**: notes by degree, a step a token (`Quarters`, `Eighths`, `Sixteenths`, `Triplets`).

| Token | Meaning |
|---|---|
| `1` to `9` | That degree; 8 is the octave above 1 |
| `'` or `,` after it | Up or down an octave, each |
| `b` or `#` before it | Down or up a semitone, each |
| `^` first | An accent: velocity 1, where a note has 0.8 |
| `~` last | A slide: the note runs its full length into the next (a mono patch glides) |
| `-` | Hold the note before a step more |
| `.` | Rest |
| `\|` | The end of a bar |

A note that doesn't slide sounds 90% of its length, so repeated notes are heard as several. `OverChords()` makes degree 1
the root of each bar's chord (a bass line that follows the chords); `Octave(n)` moves the whole line.

**`Beat`**: drums on a grid, a row a piece (`Eighths`, `Sixteenths`, `Triplets`): `x` a hit (0.75), `X` an accent (1),
`o` a ghost note (0.35), a digit `1` to `9` a hit at that many tenths (a crescendo a step at a time: a snare roll
`3333 4444 5555 6666 | 7777 8888 9999 XXXX`), `.` a rest; spaces and `|` are for reading. Every row is as long.

**`Strum`**: chords across a guitar's strings, a step a character (`Quarters`, `Eighths`, `Sixteenths`), each chord the
section's at that step.

| Character | Meaning |
|---|---|
| `D` | Down, low string to high (0.8) |
| `U` | Up, the top four strings, high to low (0.7) |
| `d`, `u` | The same, softer (0.5) |
| `x` | A muted chuck: every string for 30 ticks |
| `.` | Let ring |
| `_` | Stop the strings |

A strum rings until the next character that isn't `.`. `Voicing(Open)` lays the chord as a guitar plays it in the first
frets (`Power` and `Triad` too); `Spread(ms)` sets the time between strings (12 ms).

### Sections, tracks and placements

A song sets its `Tempo` (120), `Meter` (4/4; set it before placing any part, since parts are checked against it), `Scale`,
`Voices` (32, its chip's), and the effects of its chip's sends and master (`SpaceEffects`, `EchoEffects`,
`MasterEffects`, as factories). It holds at most 64 tracks.

A track is a patch or a kit, with its octave (degree 1's, 4 unless set), level, pan, `Humanise(timingMs, velocity)` (each
note late by up to twice `timingMs`, `timingMs` on average, never early, the same every time), `Swing(ratio)` (0.667 a
triplet feel: each off-beat step of the grid moves late before anything is spread off it, so a strum's strings move
together) and macros, each following a cue or held. Out-of-range values for these throw rather than clamp. Tracks share effects through groups (`Fx` makes the effects). A section is some bars long over
its chords; `section.Play(track, part)` places a part in it, repeated through the section if shorter, cut at its end if
longer. A placement can be `Named`, play only `When` a condition holds, follow a cue's `Level`, and `EndWith` a fill that
takes the section's last bar each pass.

**A section can sweep a macro across itself:** `section.Sweep(track, macro, from, to)` moves one of the track's patch
macros from `from` at the section's first bar to `to` at its end, over whatever the track holds it at or has it follow.
It is how a build opens or thins a filter towards a drop. In every other section the macro is the track's own again (0
if the track never set it).

`Order(a, a, b)` plays sections in turn, then from the first again; `Conduct(c => …)` chooses the next one in code, from
the `Conductor`'s current section, its repeats and the cues. It is asked as each section starts and again a beat before
it ends, and the later answer counts; the first stands if a long frame skips the last beat, so a hitch never repeats a
section. Keep it a plain function of what it is given: it is called twice a section. One that throws is asked no more
that section, which loops. With neither, the first section loops. A `Jump` lets go of what was sounding.

### Cues and conditions

A `Cue` is a level the game sets (`music.Set(Groove.Energy, 0.7f)`); a `Cue<T>` a choice of an enum's values
(`music.Set(Moods.Now, Mood.Calm)`). A cue is known by its object, so a game keeps its cues in static fields
and every song uses those; one a song doesn't use is remembered for the next. Conditions are `cue.Above(x)`,
`cue.Below(x)`, `choice.Is(value)`, combined with `&`, `|` and `!`. **`Above(x)` turns true at x and false again only
below x − 0.05** (`Below` mirrors it), so a part doesn't flap while a cue hovers.

**`OnBar()` against `OnPhrase()`.** An on-bar placement (the default) comes and goes at any bar line as its condition
changes. An on-phrase one is asked only where its part starts over, and once in plays that pass out: a four-bar melody
never starts halfway.

### The player

`MusicPlayer` plays one song at a time on a bus, a new one crossfading over the last; `Update()` once a frame. `Position`
says the section, bar and beat; `Jump`, `LoopSection`, `Mute`, `Solo`, `SetTrackGain` and `Force` (a placement held on or
off) steer it; `IsActive` and `TrackPeak` read it, for a workbench. **`Swap(song)`** takes the song rebuilt at the next
bar line, in the scale playing, carrying on in the section of the same name at the same bar (or the first section if that
name is gone), at the new tempo; the absolute bar keeps counting. It plays on a chip of its own, made from the rebuilt
song, so an edit to its groups, their effects or its voices is heard at once; the old chip lets go of its notes and rings
out under the new one for two seconds, fading over the last quarter.

The sequencer splits each block at every event, bar line and section end and hands the chip its notes between slices, so
timing is exact to the frame. It allocates nothing once playing. **Cost:** a song of eight tracks (a kit, a bass and six
four-operator parts in eighths) renders at 31× real time on a Snapdragon X laptop.

**Rendered ahead.** `new MusicPlayer(audio, bus, aheadSeconds: 0.1)` wraps each song's source in an `AheadSource`
([audio.md](audio.md), "Ahead of the speaker"), so the engine's worker renders it a tenth of a second ahead and the audio
thread only copies it: the same samples as live (`MusicPlayerTests`). The cost is latency. A cue or a command is heard
up to the lead later, so a part comes in at the bar line after the cue and the lead, and `Position`, `IsActive` and
`TrackPeak` run up to the lead ahead of the ear. A game wants it. A workbench passes 0 (the default), so what it
shows matches what is heard. Measured in a game playing eight-track songs over its own sound effects, it took the music
off the audio thread's deadline: the thread's load fell from 2.4% to 1.0% of it, its peak from 7% to 2%.

---

## 10. The workbench: `Loom.Audio.Workbench`

A window for writing music in code, kept apart from `Loom.Audio.Synth` because it is UI (SUIsei) and the synth is not.
A song stays C# in the game's own files. The workbench plays it, shows it, lets you steer it, and plays it again each time
the file is saved under `dotnet watch`. [`samples/HelloSynth`](../samples/HelloSynth) is the sample: a demo song, four
patches and a kit.

What it shows:
- **The transport.** The songs, play and stop, the position (section, bar, beat), the tempo and the scale.
- **The sections**, one button each (a click jumps there at the next bar), and a loop on the section playing.
- **The cues**, the catalog's whether the song uses them: a slider for a `Cue`, a button per value for a `Cue<T>`.
- **The tracks.** Each with mute, solo, a gain and a level meter, and under it its placements: whether each is playing,
  its condition, and auto, on and off to force it.
- **The patch bench.** A patch or a kit, played from a two-octave keyboard, the computer's keys or the kit's pads, with
  the patch's macros as sliders.
- **The pattern** of the part selected, drawn as notes over the bar.
- **A banner** with the error of a song that didn't build, while the last one plays on.

**The catalog.** A game hands the workbench a `WorkbenchCatalog`: its songs, patches and kits, each a name and a
factory, and its cues. The factories matter. Hot reload changes what a factory returns, so the workbench calls it again
after every edit rather than keeping what it made.

**Hosting it.** HelloSynth's app, cut to what matters:

```csharp
protected override void Configure(AppOptions options) => options.Audio = new AudioOptions();

protected override void LoadContent()
{
	_ui = new UIHost();
	_painter = UIPainter.Create(Engine, _ui);
	_view = _ui.CreateRoot(UILayer.Default);
	_workbench = new Workbench(Audio, Audio.CreateBus("music"), DemoCatalog.Make());
	if (Load() is { } state)
		_workbench.RestoreState(state);              // where it was before a restart
	_view.Root.Content = new WorkbenchView(_workbench.ViewModel);
	PushInputLayer(new SUIseiInputLayer(_ui));
	// ... a graph with the UI pass over the backbuffer
}

protected override void Update(FrameTime time)
{
	_workbench.HandleInput(Input);                   // the computer's keys
	_workbench.Update(time.DeltaSeconds);            // the player, the meters, and a rebuild after an edit
	// ... CaptureState() saved to a file when it changes
}
```

`WorkbenchView.TransportExtras` is a panel at the end of the transport bar for the host's own controls (a toggle for a
sound of the game's own to hear under the music, say). `CaptureState` and `RestoreState` keep the song, whether it
played and in which section, the loop, a scale played in place of the song's, the cues, the mutes, solos, gains and
forced placements, and the bench's patch, kit, octave and velocity, all by name, as JSON (`WorkbenchStateJson`).

**Keys:** `A W S E D F T G Y H U J K` play an octave of notes on the patch bench, C to
C, as a piano's keys lie. `Z` and `X` move it down and up an octave. `Space` plays or stops, and `1` to `9` jump to a
section.

### What hot reload can and can't do

- **A song, a patch and a kit are built by a method or a property, every time.** A `static readonly` field is
  initialised once and hot reload never runs its initialiser again.
- Edits inside a method's body apply live: notes, chords, levels, conditions, a patch's numbers.
- Adding a method, a property, a track or a section usually applies live too.
- Some edits can't (a changed lambda capture, a changed type's shape). `dotnet watch` then asks to restart, or restarts
  by itself when `DOTNET_WATCH_RESTART_ON_RUDE_EDIT` is `true`. The app comes back playing where it was, from its state
  file.
- A song that fails to build after an edit shows its error in the banner, and the old one plays on. Fix it and save.
- A change to a `.xaml` view reloads the view (SUIsei's own hot reload). The views are `ItemsControl`s bound to the view
  models, so a reload builds their rows again from what the view models hold.

After an edit, the song rebuilt is swapped in at the next bar (`MusicPlayer.Swap`), in the same section at the same bar.
The mutes, solos, gains and forced placements carry over by name.

---

## 11. Not built yet

- **Music in the browser costs too much to play.** There the mix runs on the frame thread, and a song of eight tracks
  costs several milliseconds of each frame; rendering it ahead on a worker (`AudioOptions.BrowserThread`) has hung the
  page. The chip, effects and offline rendering work there; a browser game should budget for music or leave it out.
- **No tempo change within a song,** and no meter change: a song has one of each. A swap may bring another.
- **The workbench is not a package.** An app references its project, and its pattern view shows a part's notes but
  doesn't edit them: the song is edited in code.
- **No vectorised inner loops.** The oversampler's half-band filter and the voices' operators are scalar; making them
  wide changes the order of their sums, and so the bits a render gives.
- **Measured on one machine.** The costs quoted here are a Snapdragon X laptop's. A denormal costs little on ARM and a
  great deal on x64; the chip and its effects keep out of that range (envelopes go idle, filters flush once a block),
  but nothing here was timed on x64 or in WebAssembly.
