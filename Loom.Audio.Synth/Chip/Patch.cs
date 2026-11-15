namespace Loom.Audio.Synth.Chip;

/// <summary>
/// An instrument for a <see cref="SynthChip"/>, as data: up to 16 operators, which of them are heard, and what the voice
/// does after them (a filter, drive, gain, pan, sends, vibrato, a pitch drop, mono and glide, macros). Write one with
/// <see cref="Build"/>.
/// <code>
/// var bell = Patch.Build("bell", p =>
/// {
/// 	var modulator = p.Sine(3.5f, Env.Decay(0.4f));
/// 	var carrier = p.Sine(1f, Env.Decay(1.5f)).ModulatedBy(modulator, 2f);
/// 	p.Out(carrier);
/// 	p.Send(ChipSend.Space, 0.3f);
/// });
/// </code>
/// </summary>
public sealed record Patch
{
	/// <summary>The most operators a patch has.</summary>
	public const int MaxOperators = 16;

	/// <summary>Its name, for messages and lists.</summary>
	public string Name { get; init; } = "";

	/// <summary>Its operators, each fed only by ones before it.</summary>
	public OperatorSpec[] Operators { get; init; } = [];

	/// <summary>The operators heard, and how loud.</summary>
	public OutputTap[] Outputs { get; init; } = [];

	/// <summary>The filter after the operators, or none.</summary>
	public FilterSpec? Filter { get; init; }

	/// <summary>How hard the voice is driven into a soft clip, 0 to 1.</summary>
	public float Drive { get; init; }

	/// <summary>The voice's gain, in decibels.</summary>
	public float GainDb { get; init; }

	/// <summary>Where it sits, -1 left to 1 right, before a note's own pan.</summary>
	public float Pan { get; init; }

	/// <summary>The share sent to the chip's space (the room).</summary>
	public float SendSpace { get; init; }

	/// <summary>The share sent to the chip's echo.</summary>
	public float SendEcho { get; init; }

	/// <summary>The vibrato's rate, in hertz.</summary>
	public float VibratoHz { get; init; }

	/// <summary>The vibrato's depth, in semitones either way.</summary>
	public float VibratoSemitones { get; init; }

	/// <summary>How long into a note before the vibrato starts, in seconds.</summary>
	public float VibratoDelaySeconds { get; init; }

	/// <summary>How far above its pitch a note starts, in semitones, falling to it: a drum's thump.</summary>
	public float PitchDropSemitones { get; init; }

	/// <summary>How long the drop takes to fall 60 dB of the way, in seconds.</summary>
	public float PitchDropSeconds { get; init; }

	/// <summary>One voice: a new note while one sounds moves it rather than starting another.</summary>
	public bool Mono { get; init; }

	/// <summary>How long a mono voice takes to slide to a new note (a time constant), in seconds.</summary>
	public float GlideSeconds { get; init; }

	/// <summary>What its macros do.</summary>
	public MacroSpec[] Macros { get; init; } = [];

	/// <summary>Writes a patch: <paramref name="build"/> adds operators and settings to a builder, then the patch is checked.</summary>
	/// <exception cref="PatchException">The patch breaks one of the builder's rules.</exception>
	public static Patch Build(string name, Action<PatchBuilder> build)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(build);
		var builder = new PatchBuilder(name);
		build(builder);
		return builder.Finish();
	}
}
