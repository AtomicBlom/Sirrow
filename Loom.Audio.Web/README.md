# Loom.Audio.Web

The browser's audio output for Loom.Audio: an AudioWorklet the mix is sent to once a frame. It needs no engine. Any
browser app (WebAssembly) with an `AudioEngine` can play through it.

```csharp
// The app's browser Main, before anything opens audio
await WebAudio.RegisterAsync();

// Anywhere after
var options = new AudioOptions();
var audio = new AudioEngine(AudioOutput.Open(options), options);
// ... once a frame, after the frame has started what it plays:
audio.Update();
```

`RegisterAsync` loads the package's page module, `_content/Loom.Audio.Web/loom-audio.js`, which loads its worklet,
`loom-audio-worklet.js`, beside it. Nothing has to be added to the page.

- **Where the mix runs:** the .NET runtime can't run on the browser's audio thread, so the mix runs on the frame's
  thread, in `AudioEngine.Update`, 50 ms ahead of the speaker. The worklet plays what it is sent, so a late frame is
  ridden out, and that lead is the web's latency. An app that doesn't call `Update` once a frame plays nothing.
- **The first gesture:** a browser lets a page make sound only after the player's first click, tap or key. Until then
  the mix keeps its time (voices advance and end) and sends nothing.
- **Debugging:** `__loomAudio()` at the console says what the output is doing: its state, its rate, and the frames sent
  and played.
