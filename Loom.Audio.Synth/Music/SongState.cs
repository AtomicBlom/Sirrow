using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// A compiled song and every array the sequencer needs to play it, made on the game thread so the audio thread takes it
/// whole (at the start, or at a swap) and allocates nothing.
/// </summary>
internal sealed class SongState
{
	public const int NoteMemory = 32;   // sounding notes remembered per placement, to let go when it stops

	public SongState(CompiledSong song)
	{
		Song = song;
		var most = Math.Max(1, song.MostPlacements);
		Cursor = new int[most];
		Active = new bool[most];
		Published = new bool[most];
		NoteIds = new int[most * NoteMemory];
		NoteEnds = new long[most * NoteMemory];
		NoteCount = new int[most];
		Forced = new PlacementState[song.Sections.Length][];
		for (var s = 0; s < Forced.Length; s++)
			Forced[s] = new PlacementState[song.Sections[s].Placements.Length];
		Cues = new float[song.Cues.Length];
		for (var c = 0; c < Cues.Length; c++)
			Cues[c] = song.Cues[c].InitialValue;
		Latches = new bool[Math.Max(1, song.LatchCount)];
		Levels = new float[ChipCore.GainSlotCount];
		Instruments = new ChipInstrumentState[song.Tracks.Length];
		for (var t = 0; t < Instruments.Length; t++)
		{
			var track = song.Tracks[t];
			Instruments[t] = track.Kit is { } kit
				? new ChipInstrumentState(kit, track.Group) { Gain = track.Gain }
				: new ChipInstrumentState(track.Patch!, track.Group) { Gain = track.Gain };
		}
		Peaks = new float[song.Tracks.Length];
		Muted = new bool[song.Tracks.Length];
		Soloed = new bool[song.Tracks.Length];
		TrackGain = new float[song.Tracks.Length];
		Array.Fill(TrackGain, 1f);
	}

	public CompiledSong Song { get; }
	public int[] SectionMap { get; set; } = [];   // a swap: each old section's index here, by name, or 0

	// Per placement of the section playing
	public int[] Cursor { get; }
	public bool[] Active { get; }
	public bool[] Published { get; }              // Active as the game reads it
	public int[] NoteIds { get; }
	public long[] NoteEnds { get; }
	public int[] NoteCount { get; }

	public PlacementState[][] Forced { get; }
	public float[] Cues { get; }                  // by slot; the game writes them whole (Volatile)
	public bool[] Latches { get; }
	public float[] Levels { get; }                // by gain slot, eased
	public ChipInstrumentState[] Instruments { get; }
	public float[] Peaks { get; }                 // by track, the last block's, as the game reads them
	public bool[] Muted { get; }
	public bool[] Soloed { get; }
	public float[] TrackGain { get; }
}
