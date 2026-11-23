namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>How busy a chip was at its last block.</summary>
/// <param name="ActiveVoices">Voices sounding.</param>
/// <param name="ActiveOperators">Their operators, all told: the chip's cost.</param>
/// <param name="StolenVoices">Voices taken from a sounding note for a new one, since the chip began.</param>
/// <param name="DroppedNotes">
/// Notes and hits that found every voice already fading out for another and so never sounded, since the chip began: more
/// voices are wanted if it climbs.
/// </param>
public readonly record struct ChipStats(int ActiveVoices, int ActiveOperators, int StolenVoices, int DroppedNotes);
