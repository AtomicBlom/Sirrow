namespace Loom.Audio.Synth.Music;

/// <summary>A placement as the sequencer reads it: its events through the whole section, a fill's in its last bar.</summary>
internal sealed class CompiledPlacement
{
	public Placement Source { get; init; } = null!;
	public string Name { get; init; } = "";
	public int Track { get; init; }
	public SongEvent[] Events { get; init; } = [];
	public int LoopTicks { get; init; }                  // the part's own length in whole bars: where an on-phrase part may start
	public ConditionOp[] Condition { get; init; } = [];  // empty: always
	public int LevelSlot { get; init; } = -1;
	public int GainSlot { get; init; } = -1;
	public bool OnPhrase { get; init; }
}
