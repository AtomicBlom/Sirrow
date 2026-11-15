namespace Loom.Audio.Synth.Chip;

/// <summary>The two shared effects every <see cref="SynthChip"/> has, which a patch sends a share of its voices to.</summary>
public enum ChipSend
{
	/// <summary>The room: <see cref="ChipOptions.SpaceEffects"/>, a reverb by default.</summary>
	Space,
	/// <summary>The echo: <see cref="ChipOptions.EchoEffects"/>, a delay by default.</summary>
	Echo,
}
