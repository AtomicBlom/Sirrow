namespace Loom.Audio.Synth.Chip;

/// <summary>The shared buses every <see cref="SynthChip"/> has, which a patch sends a share of its voices to.</summary>
public enum ChipSend
{
	/// <summary>The room: <see cref="ChipOptions.SpaceEffects"/>, a reverb by default.</summary>
	Space,
	/// <summary>The echo: <see cref="ChipOptions.EchoEffects"/>, a delay by default.</summary>
	Echo,
	/// <summary>
	/// The key: never heard, it is what keyed effects listen to (<see cref="Effects.IKeyedEffect"/>), wherever they sit in
	/// the chip: a compressor on the bass's group, keyed, turns the bass down while the kick that sends here sounds.
	/// </summary>
	Key,
}
