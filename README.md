# Sirrow

Audio for .NET: a low-latency mixer with buses and pooled voices, clips and music streamed from WAV, Ogg Vorbis and
FLAC, 3D voices, and **sources**, sound a program writes itself sample by sample: software synthesizers, procedural
engine notes, generated music. On top of it, a 16-operator synthesizer chip with patches and drum kits, effects, and
music written as data or code with a sequencer. It needs no engine and no window. Sirrow was split out of the
[Kaldra engine](https://github.com/AtomicBlom/Kaldra), once called Loom. Its packages are `BinaryVibrance.Sirrow` and the rest.

| Project | What it is |
|---|---|
| `BinaryVibrance.Sirrow` | The mixer: buses, voices, clips, streams, sources, effects, 3D. The BCL only. |
| `BinaryVibrance.Sirrow.Sdl` | The desktop's output, through SDL 3 (SDL3.dll from `BinaryVibrance.Kaldra.SDL3`). |
| `BinaryVibrance.Sirrow.Android` | Android's output, through AAudio. |
| `BinaryVibrance.Sirrow.Web` | The browser's output, an AudioWorklet. |
| `BinaryVibrance.Sirrow.Synth` | Synthesis and music over the mixer, and offline rendering to measure sound or save it as a WAV. Its package carries the music generator. |
| `BinaryVibrance.Sirrow.Music.Generator` | The source generator that makes a project's patch and song files into C#. |
| `samples/HelloSynth` | A music project: a song, four patches and a kit. |

## Playing sound

```csharp
// Each platform's entry point registers its output, once
SdlAudio.Register();            // desktop
AndroidAudio.Register();        // Android
await WebAudio.RegisterAsync(); // browser

// Then, the same everywhere
var options = new AudioOptions();
var audio = new AudioEngine(AudioOutput.Open(options), options);
var sfx = audio.CreateBus("sfx");
audio.Play(audio.LoadWav(File.ReadAllBytes("hit.wav")), sfx, volume: 0.8f);
// ... and once a frame:
audio.Update();
```

`docs/audio.md` is the guide to the mixer, its threads and each platform's output; `docs/synth.md` is the guide to
synthesis and music.

## Building

```
dotnet build BinaryVibrance.Sirrow.slnx
dotnet run --project BinaryVibrance.Sirrow.Tests
```

`BinaryVibrance.Sirrow.Android` is in no solution, so a desktop build never needs the android workload:
`dotnet build BinaryVibrance.Sirrow.Android` builds it where the workload is installed.

## Licence

MIT (`LICENSE`).
