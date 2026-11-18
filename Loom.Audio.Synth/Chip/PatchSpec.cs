namespace Loom.Audio.Synth.Chip;

/// <summary>
/// A patch as data that can be changed: its operators (each an <see cref="OperatorSpec"/> whose modulators and drivers
/// name operators before it by index), what is heard of them, and what the voice does after. A tool edits one and builds
/// a <see cref="Patch"/> from it with <see cref="ToPatch"/>, which checks every rule <see cref="Patch.Build"/> does.
/// </summary>
public sealed class PatchSpec
{
	/// <summary>Its name, as <see cref="Patch.Name"/>.</summary>
	public string Name { get; set; } = "";

	/// <summary>The operators, in the order they render: one is modulated or driven only by one before it.</summary>
	public List<OperatorSpec> Operators { get; } = [];

	/// <summary>The operators heard, each at a level and a pan.</summary>
	public List<OutputTap> Outputs { get; } = [];

	/// <summary>The filter after the mixed operators, or null for none.</summary>
	public FilterSpec? Filter { get; set; }

	/// <summary>A soft clip after the filter, 0 to 1.</summary>
	public float Drive { get; set; }

	/// <summary>The voice's gain, in decibels.</summary>
	public float GainDb { get; set; }

	/// <summary>Where the voice sits, -1 left to 1 right.</summary>
	public float Pan { get; set; }

	/// <summary>The sends to the chip's space, echo and key.</summary>
	public float SendSpace { get; set; }

	/// <inheritdoc cref="SendSpace"/>
	public float SendEcho { get; set; }

	/// <inheritdoc cref="SendSpace"/>
	public float SendKey { get; set; }

	/// <summary>The vibrato's rate, depth in semitones, and how long after a note's start it comes in.</summary>
	public float VibratoHz { get; set; }

	/// <inheritdoc cref="VibratoHz"/>
	public float VibratoSemitones { get; set; }

	/// <inheritdoc cref="VibratoHz"/>
	public float VibratoDelaySeconds { get; set; }

	/// <summary>The tremolo's rate and depth, 0 to 1.</summary>
	public float TremoloHz { get; set; }

	/// <inheritdoc cref="TremoloHz"/>
	public float TremoloDepth { get; set; }

	/// <summary>How far each note bends down to its pitch from above as it starts, and over how long.</summary>
	public float PitchDropSemitones { get; set; }

	/// <inheritdoc cref="PitchDropSemitones"/>
	public float PitchDropSeconds { get; set; }

	/// <summary>Whether it plays one voice, a new note moving it, gliding over <see cref="GlideSeconds"/>.</summary>
	public bool Mono { get; set; }

	/// <inheritdoc cref="Mono"/>
	public float GlideSeconds { get; set; }

	/// <summary>Its macros: what each moves, from what to what.</summary>
	public List<MacroSpec> Macros { get; } = [];

	/// <summary>A spec holding everything <paramref name="patch"/> is, to change.</summary>
	public static PatchSpec From(Patch patch)
	{
		ArgumentNullException.ThrowIfNull(patch);
		var spec = new PatchSpec
		{
			Name = patch.Name,
			Filter = patch.Filter,
			Drive = patch.Drive,
			GainDb = patch.GainDb,
			Pan = patch.Pan,
			SendSpace = patch.SendSpace,
			SendEcho = patch.SendEcho,
			SendKey = patch.SendKey,
			VibratoHz = patch.VibratoHz,
			VibratoSemitones = patch.VibratoSemitones,
			VibratoDelaySeconds = patch.VibratoDelaySeconds,
			TremoloHz = patch.TremoloHz,
			TremoloDepth = patch.TremoloDepth,
			PitchDropSemitones = patch.PitchDropSemitones,
			PitchDropSeconds = patch.PitchDropSeconds,
			Mono = patch.Mono,
			GlideSeconds = patch.GlideSeconds,
		};
		spec.Operators.AddRange(patch.Operators);
		spec.Outputs.AddRange(patch.Outputs);
		spec.Macros.AddRange(patch.Macros);
		return spec;
	}

	/// <summary>A copy to change apart from this one.</summary>
	public PatchSpec Clone()
	{
		var copy = new PatchSpec();
		copy.CopyFrom(this);
		return copy;
	}

	private void CopyFrom(PatchSpec other)
	{
		Name = other.Name;
		About = other.About;
		Filter = other.Filter;
		Drive = other.Drive;
		GainDb = other.GainDb;
		Pan = other.Pan;
		SendSpace = other.SendSpace;
		SendEcho = other.SendEcho;
		SendKey = other.SendKey;
		VibratoHz = other.VibratoHz;
		VibratoSemitones = other.VibratoSemitones;
		VibratoDelaySeconds = other.VibratoDelaySeconds;
		TremoloHz = other.TremoloHz;
		TremoloDepth = other.TremoloDepth;
		PitchDropSemitones = other.PitchDropSemitones;
		PitchDropSeconds = other.PitchDropSeconds;
		Mono = other.Mono;
		GlideSeconds = other.GlideSeconds;
		Operators.AddRange(other.Operators);
		Outputs.AddRange(other.Outputs);
		Macros.AddRange(other.Macros);
	}

	/// <summary>Builds the patch it describes, checked as <see cref="Patch.Build"/> checks one.</summary>
	/// <exception cref="PatchException">It breaks one of the rules: an operator routed from one after it, nothing heard, and the rest.</exception>
	public Patch ToPatch() => Patch.Build(Name, p =>
	{
		var ops = new List<Op>(Operators.Count);
		for (var i = 0; i < Operators.Count; i++)
		{
			var spec = Operators[i];
			var op = p.Add(spec with { Modulators = [], Drivers = [] });
			foreach (var modulation in spec.Modulators)
				op.ModulatedBy(Before(ops, modulation.Operator, i, "modulated"), modulation.Index);
			foreach (var driver in spec.Drivers)
				op.DrivenBy(Before(ops, driver, i, "driven"));
			ops.Add(op);
		}
		p.Out([.. Outputs.Select(tap => Of(ops, tap.Operator, "heard").At(tap.Level, tap.Pan))]);
		if (Filter is { } filter)
			p.Filter(filter);
		p.Drive(Drive);
		p.Gain(GainDb);
		p.Pan(Pan);
		p.Send(ChipSend.Space, SendSpace);
		p.Send(ChipSend.Echo, SendEcho);
		p.Send(ChipSend.Key, SendKey);
		p.Vibrato(VibratoHz, VibratoSemitones, VibratoDelaySeconds);
		p.Tremolo(TremoloHz, TremoloDepth);
		p.PitchDrop(PitchDropSemitones, PitchDropSeconds);
		if (Mono)
			p.Mono(GlideSeconds);
		foreach (var macro in Macros)
			p.Macro(macro.Macro, macro.Target, macro.From, macro.To, macro.Operator >= 0 ? Of(ops, macro.Operator, "turned by a macro") : null);
	});

	private Op Before(List<Op> made, int index, int at, string how) =>
		index >= 0 && index < at
			? made[index]
			: throw new PatchException(Name, $"operator {at} is {how} by operator {index}, and an operator is {how} only by one before it.");

	private Op Of(List<Op> made, int index, string how) =>
		(uint)index < (uint)made.Count
			? made[index]
			: throw new PatchException(Name, $"operator {index} is {how}, and the patch has {made.Count} operators.");

	/// <summary>What the patch is, in words, as its file and its code's comment say it; empty when nothing does.</summary>
	public string About { get; set; } = "";
}
