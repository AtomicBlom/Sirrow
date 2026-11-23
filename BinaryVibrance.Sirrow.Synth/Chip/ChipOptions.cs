using BinaryVibrance.Sirrow.Synth.Effects;

namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>How a <see cref="SynthChip"/> is sized and routed: its voices, groups, the effects of its two sends and its master.</summary>
public sealed record ChipOptions
{
	/// <summary>How many notes sound at once; past it the voice released longest, else the oldest, is stolen. Default 32.</summary>
	public int Voices { get; init; } = 32;

	/// <summary>The groups instruments play through, each with its own effects. Default one, "main", with none.</summary>
	public ChipGroup[] Groups { get; init; } = [new("main")];

	/// <summary>The space send's effects: a <see cref="Reverb"/> as it comes (all wet) by default.</summary>
	public Func<AudioEffect>[] SpaceEffects { get; init; } = [DefaultSpace];

	/// <summary>The echo send's effects: a <see cref="Delay"/>, all wet, by default.</summary>
	public Func<AudioEffect>[] EchoEffects { get; init; } = [DefaultEcho];

	/// <summary>Effects over the whole chip, after the groups and sends. Default none.</summary>
	public Func<AudioEffect>[] MasterEffects { get; init; } = [];

	/// <summary>How many instruments and kits can be added. Default 64.</summary>
	public int MaxInstruments { get; init; } = 64;

	/// <summary>How many calls (notes, hits, macros) can wait for the audio thread at once. Default 1024.</summary>
	public int EventCapacity { get; init; } = 1024;

	private static AudioEffect DefaultSpace() => new Reverb();

	private static AudioEffect DefaultEcho()
	{
		var delay = new Delay();
		delay.Wet.Value = 1f;
		delay.Dry.Value = 0f;
		return delay;
	}
}
