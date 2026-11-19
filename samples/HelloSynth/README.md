# HelloSynth

A music project to open in `loom-workbench`: a demo song in A minor, four patches, a drum kit, two cues and a macro.
It has no app of its own. `loom-workbench` compiles it, plays it, and compiles it again each time a file is saved, and
`loom-workbench new` copies it as the start of a project of your own.

## Open it

From Loom's root:

```
dotnet run --project Loom.Audio.Workbench.App -- samples/HelloSynth
```

The window opens at once and plays the demo once the project has compiled (a few seconds the first time, while a
design-time build reads the project). Space plays and stops, `1` and `2` jump to the verse and the chorus, and the keys
from `A` to `K` play the patch chosen. Ctrl K finds anything.

To start a project of your own from this one, renamed for its folder:

```
dotnet run --project Loom.Audio.Workbench.App -- new ../MyTunes
```

## What's data and what's C#

| File | What it is |
|---|---|
| `Songs/demo.song.json` | The song (`loom-song/2`): tempo, scale, tracks, the verse (twice) and chorus with their exits, each part's notation and condition. The roll edits it. |
| `Patches/*.patch.json` | The patches (`loom-patch/1`): each operator named, routed by name. The patch designer edits them. |
| `Catalog.cs` | What the workbench finds: `[assembly: MusicCatalog(typeof(Catalog))]` on a class listing the songs, patches, kits and cues. |
| `Cues.cs`, `Mood.cs` | The cues the song listens to: `intensity`, a level (the lead comes in with it), and `mood`, one of `Mood`'s values (a motif for each). |
| `Kits.cs` | The drum kit, as code: a kit of patches, so it needs no samples. |
| `Macros.cs` | The `bright` macro the lead's patch reads, which its track follows from the intensity. |

As the project builds, Loom's music generator (`Loom.Audio.Music.Generator`, the project's analyzer) turns each JSON
file into the C# a person would have written: `Songs.Demo.Build()`, `Patches.ElectricPiano`. A game builds and runs
that C#, so nothing reads JSON at run time. Any piece can be ejected to C# from the workbench, to do something the
files can't say, and brought back as data later.

`docs/synth.md`, sections 10 and 11, has the rest.
