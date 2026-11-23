# Sirrow

Audio for .NET: a low-latency mixer with buses and pooled voices, clips and music streamed from WAV, Ogg Vorbis and
FLAC, 3D voices, and **sources**, sound a program writes itself sample by sample: software synthesizers, procedural
engine notes, generated music. With it, effects for buses (reverb, delay, chorus, compression, limiting, EQ,
distortion), the DSP building blocks sources are made of, and rendering offline to measure sound or save it as a WAV.
It needs no engine and no window. A synthesizer chip with patches, drum kits and songs is
[Lunelle](https://github.com/AtomicBlom/Lunelle), built on Sirrow. Sirrow was split out of the
[Kaldra engine](https://github.com/AtomicBlom/Kaldra), once called Loom. Its packages are `BinaryVibrance.Sirrow` and the rest.

| Project | What it is |
|---|---|
| `BinaryVibrance.Sirrow` | The mixer: buses, voices, clips, streams, sources, effects, 3D; DSP building blocks; offline rendering, WAV files and measures. The BCL only. |
| `BinaryVibrance.Sirrow.Sdl` | The desktop's output, through SDL 3 (SDL3.dll from `BinaryVibrance.Kaldra.SDL3`). |
| `BinaryVibrance.Sirrow.Android` | Android's output, through AAudio. |
| `BinaryVibrance.Sirrow.Web` | The browser's output, an AudioWorklet. |

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

`docs/audio.md` is the guide to the mixer, its threads and each platform's output; `docs/effects.md` to rendering
offline, the DSP building blocks and the effects.

## Building

```
dotnet build BinaryVibrance.Sirrow.slnx
dotnet run --project BinaryVibrance.Sirrow.Tests
```

`BinaryVibrance.Sirrow.Android` is in no solution, so a desktop build never needs the android workload:
`dotnet build BinaryVibrance.Sirrow.Android` builds it where the workload is installed.

## Licence

MIT (`LICENSE`).
