# Loom.Audio.Android

Android's audio output for Loom.Audio, through AAudio: a low-latency stream on the default output in 32-bit float
stereo, asking for exclusive use of it, with its buffer at two bursts. It needs no engine. Any Android app with an
`AudioEngine` can play through it.

```csharp
// The app's Android entry point (its activity), before anything opens audio
AndroidAudio.Register();

// Anywhere after
var options = new AudioOptions();
var audio = new AudioEngine(AudioOutput.Open(options), options);
// ... once a frame:
audio.Update();
```

- **The callback shim:** AAudio can't call C# directly, so it calls `libloomaaudio.so` (`Native/loom_aaudio.c`), which
  forwards to the device. The library is built per ABI (arm64-v8a, x86_64) and checked in, so building needs no NDK;
  `Native/build.ps1` rebuilds it after a change to the C.
- **When the output changes:** if the output goes (headphones unplugged, a Bluetooth device dropping), the stream is
  reopened on the new default output at the same rate, from a thread of its own, since AAudio forbids reopening from
  its callbacks. What happens is logged under the `Loom` tag (`adb logcat Loom:V *:S`).
- **Underruns:** `IAudioDevice.Underruns` is AAudio's count of xruns, each a click or a gap.
