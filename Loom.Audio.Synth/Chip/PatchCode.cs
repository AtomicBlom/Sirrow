using System.Globalization;
using System.Text;

using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Chip;

/// <summary>
/// Writes a patch as the C# that builds it: a <c>Patch.Build</c> property, as a person would have written it, for a tool
/// that designs patches to hand back as code. What it writes builds a patch equal to the one it was given.
/// </summary>
public static class PatchCode
{
	private static readonly HashSet<string> Keywords =
	[
		"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
		"decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
		"fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
		"namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
		"readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
		"switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
		"virtual", "void", "volatile", "while", "p",
	];

	/// <summary>
	/// The C# for <paramref name="patch"/>: <c>public static Patch Name => Patch.Build("name", p => { … });</c>.
	/// </summary>
	/// <param name="patch">The patch.</param>
	/// <param name="property">The property's name; the patch's name in PascalCase when null.</param>
	/// <param name="names">A name for each operator's variable, by index; by kind ("sine1", "noise2") when null or short.</param>
	/// <param name="macroName">How a macro is named in code ("Macros.Bright"); <c>new Macro(n)</c> when null or it gives null.</param>
	/// <param name="indent">One level of indent.</param>
	public static string Write(Patch patch, string? property = null, IReadOnlyList<string>? names = null,
		Func<int, string?>? macroName = null, string indent = "\t") =>
		Write(PatchSpec.From(patch), property, names, macroName, indent);

	/// <inheritdoc cref="Write(Patch, string?, IReadOnlyList{string}?, Func{int, string?}?, string)"/>
	public static string Write(PatchSpec spec, string? property = null, IReadOnlyList<string>? names = null,
		Func<int, string?>? macroName = null, string indent = "\t")
	{
		ArgumentNullException.ThrowIfNull(spec);
		var ops = spec.Operators;
		var vars = OperatorNames(ops, names);

		// The builder numbers operators as they are made, so every one up to the last that another reads (or that isn't
		// heard) gets a variable; those after it are made inline in Out, if Out lists them in their own order
		var referenced = new bool[ops.Count];
		foreach (var op in ops)
		{
			foreach (var m in op.Modulators)
				referenced[m.Operator] = true;
			foreach (var d in op.Drivers)
				referenced[d] = true;
		}
		foreach (var macro in spec.Macros)
			if (macro.Operator >= 0)
				referenced[macro.Operator] = true;
		var heard = new bool[ops.Count];
		foreach (var tap in spec.Outputs)
			heard[tap.Operator] = true;
		var lastVar = -1;
		for (var i = 0; i < ops.Count; i++)
			if (referenced[i] || !heard[i])
				lastVar = i;
		var declared = new bool[ops.Count];
		for (var i = 0; i <= lastVar; i++)
			declared[i] = true;
		var inline = Enumerable.Range(0, ops.Count).Where(i => !declared[i]).ToArray();
		var outOrder = spec.Outputs.Select(t => t.Operator).Where(i => !declared[i]).ToArray();
		if (!inline.SequenceEqual(outOrder))
			Array.Fill(declared, true);

		var lines = new List<string>();
		for (var i = 0; i < ops.Count; i++)
			if (declared[i])
				lines.Add($"var {vars[i]} = {Expression(ops[i], vars)};");
		var taps = spec.Outputs.Select(tap =>
		{
			var made = declared[tap.Operator] ? vars[tap.Operator] : Expression(ops[tap.Operator], vars);
			return tap.Level == 1f && tap.Pan == 0f ? made : $"{made}.At({F(tap.Level)}{(tap.Pan != 0f ? ", " + F(tap.Pan) : "")})";
		}).ToArray();
		if (taps.Length > 0)
		{
			var one = $"p.Out({string.Join(", ", taps)});";
			lines.Add(one.Length <= 110 ? one : $"p.Out(\n{indent}{string.Join($",\n{indent}", taps)});");
		}
		if (spec.Filter is { } filter)
			lines.Add($"p.Filter({FilterCode(filter)});");
		if (spec.PitchDropSemitones != 0f)
			lines.Add($"p.PitchDrop({F(spec.PitchDropSemitones)}, {F(spec.PitchDropSeconds)});");
		if (spec.VibratoHz != 0f || spec.VibratoSemitones != 0f || spec.VibratoDelaySeconds != 0f)
			lines.Add($"p.Vibrato({F(spec.VibratoHz)}, {F(spec.VibratoSemitones)}{(spec.VibratoDelaySeconds != 0f ? ", " + F(spec.VibratoDelaySeconds) : "")});");
		if (spec.TremoloHz != 0f || spec.TremoloDepth != 0f)
			lines.Add($"p.Tremolo({F(spec.TremoloHz)}, {F(spec.TremoloDepth)});");
		if (spec.Drive != 0f)
			lines.Add($"p.Drive({F(spec.Drive)});");
		if (spec.Mono)
			lines.Add(spec.GlideSeconds != 0f ? $"p.Mono({F(spec.GlideSeconds)});" : "p.Mono();");
		if (spec.SendSpace != 0f)
			lines.Add($"p.Send(ChipSend.Space, {F(spec.SendSpace)});");
		if (spec.SendEcho != 0f)
			lines.Add($"p.Send(ChipSend.Echo, {F(spec.SendEcho)});");
		if (spec.SendKey != 0f)
			lines.Add($"p.Send(ChipSend.Key, {F(spec.SendKey)});");
		if (spec.GainDb != 0f)
			lines.Add($"p.Gain({F(spec.GainDb)});");
		if (spec.Pan != 0f)
			lines.Add($"p.Pan({F(spec.Pan)});");
		foreach (var macro in spec.Macros)
		{
			var name = macroName?.Invoke(macro.Macro.Id) ?? $"new Macro({macro.Macro.Id})";
			var op = macro.Operator >= 0 ? ", " + vars[macro.Operator] : "";
			lines.Add($"p.Macro({name}, MacroTarget.{macro.Target}, {F(macro.From)}, {F(macro.To)}{op});");
		}

		var text = new StringBuilder();
		text.Append($"public static Patch {property ?? Pascal(spec.Name)} => Patch.Build(\"{spec.Name}\", p =>\n{{\n");
		foreach (var line in lines)
			text.Append(indent).Append(line.Replace("\n", "\n" + indent, StringComparison.Ordinal)).Append('\n');
		text.Append("});");
		return text.ToString();
	}

	/// <summary>A name in PascalCase: "electric-piano" is "ElectricPiano".</summary>
	public static string Pascal(string name) =>
		string.Concat(name.Split(['-', '_', ' ', '.'], StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

	// A float as C# writes it: the shortest that reads back as the same float
	private static string F(float value) => value.ToString(CultureInfo.InvariantCulture) + "f";

	private static string Envelope(EnvelopeShape e) =>
		e == EnvelopeShape.Gate ? "Env.Gate"
		: e == EnvelopeShape.Struck(e.Decay) ? $"Env.Decay({F(e.Decay)})"
		: $"Env.Adsr({F(e.Attack)}, {F(e.Decay)}, {F(e.Sustain)}, {F(e.Release)})";

	// ", level, velocity: v", leaving off what is the default
	private static string LevelArgs(OperatorSpec o, bool force)
	{
		var text = force || o.Level != 1f || o.Velocity != 0f ? ", " + F(o.Level) : "";
		return o.Velocity != 0f ? text + $", velocity: {F(o.Velocity)}" : text;
	}

	private static string Expression(OperatorSpec o, string[] vars)
	{
		var pulseWidth = o.Kind == OperatorKind.Wave && o.Waveform == Waveform.Pulse && o.PulseWidth != 0.5f;
		var text = o.Kind switch
		{
			OperatorKind.Sine => $"p.Sine({F(o.Ratio)}, {Envelope(o.Envelope)}{LevelArgs(o, false)})",
			OperatorKind.Wave => $"p.Wave(Waveform.{o.Waveform}, {F(o.Ratio)}, {Envelope(o.Envelope)}{LevelArgs(o, pulseWidth)}{(pulseWidth ? $", pulseWidth: {F(o.PulseWidth)}" : "")})",
			OperatorKind.Noise => $"p.Noise({Envelope(o.Envelope)}{LevelArgs(o, o.Pink)}{(o.Pink ? ", pink: true" : "")})",
			OperatorKind.Level => $"p.Level({Envelope(o.Envelope)}{LevelArgs(o, false)})",
			OperatorKind.Sample => $"p.Sample(samples, {F(o.Level)}, velocity: {F(o.Velocity)}{(o.OneShot ? ", oneShot: true" : "")})",
			OperatorKind.String => $"p.String({F(o.Ratio)}, decaySeconds: {F(o.DecaySeconds)}, damping: {F(o.Damping)}, pick: {F(o.Pick)})",
			OperatorKind.Tube => o.Ratio != 1f ? $"p.Tube({F(o.Ratio)})" : "p.Tube()",
			OperatorKind.Modes => $"p.Modes({string.Join(", ", o.Modes.Select(ModeCode))})",
			_ => throw new ArgumentOutOfRangeException(nameof(o), o.Kind, "An operator of a kind this doesn't know."),
		};
		if (o.FixedHz != 0f && o.Kind != OperatorKind.Modes)
			text += $".Fixed({F(o.FixedHz)})";
		if (o.DetuneCents != 0f)
			text += $".Detune({F(o.DetuneCents)})";
		if (o.StartPhase != 0f)
			text += $".Phase({F(o.StartPhase)})";
		if (o.Feedback != 0f)
			text += $".Feedback({F(o.Feedback)})";
		foreach (var m in o.Modulators)
			text += $".ModulatedBy({vars[m.Operator]}, {F(m.Index)})";
		if (o.Drivers.Length > 0)
			text += $".DrivenBy({string.Join(", ", o.Drivers.Select(d => vars[d]))})";
		if (o.KeyLevelDb != 0f)
			text += $".KeyLevel({F(o.KeyLevelDb)})";
		if (o.KeyDecay != 0f)
			text += $".KeyDecay({F(o.KeyDecay)})";
		return text;
	}

	private static string ModeCode(Mode m)
	{
		var level = m.Level != 1f ? ", " + F(m.Level) : "";
		return m.Hz > 0f ? $"Mode.Fixed({F(m.Hz)}, {F(m.DecaySeconds)}{level})" : $"Mode.At({F(m.Ratio)}, {F(m.DecaySeconds)}{level})";
	}

	private static string FilterCode(FilterSpec f)
	{
		var text = new StringBuilder($"Filter.{f.Kind}({F(f.CutoffHz)}");
		if (f.Q != 0.7f)
			text.Append($", q: {F(f.Q)}");
		if (f.KeyTrack != 0f)
			text.Append($", keyTrack: {F(f.KeyTrack)}");
		if (f.Velocity != 0f)
			text.Append($", velocity: {F(f.Velocity)}");
		if (f.EnvelopeOctaves != 0f)
			text.Append($", envelopeOctaves: {F(f.EnvelopeOctaves)}, envelope: {Envelope(f.Envelope)}");
		return text.Append(')').ToString();
	}

	/// <summary>
	/// The variable each operator is written with: the name given, made a C# identifier, else its kind and a count
	/// ("sine1", "noise2"); never two alike.
	/// </summary>
	public static string[] OperatorNames(IReadOnlyList<OperatorSpec> ops, IReadOnlyList<string>? given = null)
	{
		var names = new string[ops.Count];
		var used = new HashSet<string>();
		var counts = new Dictionary<string, int>();
		for (var i = 0; i < ops.Count; i++)
		{
			var name = given is not null && i < given.Count ? Identifier(given[i]) : "";
			if (name.Length == 0)
			{
				var kind = ops[i].Kind == OperatorKind.Wave ? ops[i].Waveform.ToString().ToLowerInvariant() : ops[i].Kind.ToString().ToLowerInvariant();
				counts[kind] = counts.GetValueOrDefault(kind) + 1;
				name = kind + counts[kind];
			}
			var unique = name;
			for (var n = 2; !used.Add(unique); n++)
				unique = name + n;
			names[i] = unique;
		}
		return names;
	}

	private static string Identifier(string name)
	{
		var text = new string([.. name.Where(c => char.IsLetterOrDigit(c) || c == '_')]);
		if (text.Length == 0)
			return "";
		if (char.IsDigit(text[0]))
			text = "_" + text;
		return Keywords.Contains(text) ? text + "Op" : text;
	}
}
