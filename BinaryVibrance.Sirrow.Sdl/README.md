# BinaryVibrance.Sirrow.Sdl

The desktop's audio output for Sirrow, through SDL 3: the system's default playback device, opened at its own rate
in 32-bit float stereo with the smallest buffer the platform takes. It needs no engine. Any desktop app with an
`AudioEngine` can play through it.

```csharp
// The app's desktop entry point, before anything opens audio
SdlAudio.Register();

// Anywhere after
var options = new AudioOptions();
var audio = new AudioEngine(AudioOutput.Open(options), options);
// ... once a frame:
audio.Update();
```

- **SDL 3** comes from the `BinaryVibrance.Kaldra.SDL3` package, SDL's own release builds for win-x64 and win-arm64.
  Where SDL3.dll is missing or no device opens, `AudioOutput.Open` goes on to the next registered output, or plays
  silent on a `NullAudioDevice` that says why.
- **Mixing on SDL's thread:** SDL calls back on its own audio thread for each buffer, and the mixer renders there.
- **Following the default device:** a stream on the default device follows the system's default as it changes
  (headphones plugged in), which SDL does on its own.
- **Sharing SDL:** other code in the process may use the same SDL, a desktop host reading gamepads for one. SDL counts
  each subsystem's starts, and the device starts and stops only its own.
