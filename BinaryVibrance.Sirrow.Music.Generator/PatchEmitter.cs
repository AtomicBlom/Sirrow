using System.Text;

namespace BinaryVibrance.Sirrow.Music.Generator;

/// <summary>
/// A <c>sirrow-patch/1</c> file as the property that builds it: <c>public static Patch Name => Patch.Build("name", p =>
/// { … });</c>, written as Sirrow's <c>PatchCode</c> writes a patch, so the two build the same patch. A file whose patch is
/// code (<c>"code"</c>) is a partial property's declaration, for the game's own C# to implement.
/// </summary>
internal sealed class PatchEmitter(MusicContext context)
{
	private sealed class Op
	{
		public string Name = "";
		public string Kind = "sine";
		public string Ratio = "1";
		public string? Fixed, Detune, Phase, Velocity, Feedback, KeyLevel, KeyDecay;
		public string Envelope = "gate";
		public string Level = "1";
		public string PulseWidth = "0.5";
		public bool Pink, OneShot;
		public string DecaySeconds = "2", Damping = "0.5", Pick = "0.2";
		public List<string> Modes = [];
		public List<(int Op, string Index)> Modulators = [];
		public List<int> Drivers = [];
	}

	/// <summary>The property for the patch file <paramref name="root"/>, indented by <paramref name="indent"/>.</summary>
	/// <exception cref="JsonError">The file says something that isn't a patch.</exception>
	public IEnumerable<string> Property(Json root, string property, string indent)
	{
		var name = Need(root, "name").Str ?? throw new JsonError("a patch's \"name\" is text", root["name"]!.Span);

		var lines = new List<string>();
		if (root["about"]?.Str is { } about)
			lines.AddRange(Code.Comment(about, indent));
		if (root["code"]?.Str is { } code)
		{
			lines.Add($"{indent}// Code of the game's own ({code}), declared here and built in its C#");
			lines.Add($"{indent}public static partial Patch {Code.Identifier(code)} {{ get; }}");
			return lines;
		}

		var ops = new List<Op>();
		var index = new Dictionary<string, int>();
		int OpNamed(Json? named, string what)
		{
			var opName = named?.Str;
			return opName is not null && index.TryGetValue(opName, out var i)
				? i
				: throw new JsonError($"{what} names operator '{opName}', which no operator before it is", named?.Span ?? root.Span);
		}
		foreach (var element in root["operators"]?.Items ?? [])
		{
			var op = new Op
			{
				Name = element["name"]?.Str ?? $"op{ops.Count + 1}",
				Kind = element["kind"]?.Str ?? throw new JsonError("an operator has a \"kind\"", element.Span),
				Ratio = element["ratio"]?.Num ?? "1",
				Fixed = element["fixed"]?.Num,
				Detune = element["detune"]?.Num,
				Phase = element["phase"]?.Num,
				Envelope = element["envelope"]?.Str ?? "gate",
				Level = element["level"]?.Num ?? "1",
				Velocity = element["velocity"]?.Num,
				Feedback = element["feedback"]?.Num,
				PulseWidth = element["pulseWidth"]?.Num ?? "0.5",
				Pink = element["pink"]?.True ?? false,
				OneShot = element["oneShot"]?.True ?? false,
				DecaySeconds = element["decaySeconds"]?.Num ?? "2",
				Damping = element["damping"]?.Num ?? "0.5",
				Pick = element["pick"]?.Num ?? "0.2",
				Modes = [.. (element["modes"]?.Items ?? []).Select(m => m.Str ?? "")],
				KeyLevel = element["keyLevel"]?.Num,
				KeyDecay = element["keyDecay"]?.Num,
			};
			if (op.Kind is not ("sine" or "saw" or "pulse" or "noise" or "level" or "string" or "tube" or "modes"))
				throw new JsonError($"\"{op.Kind}\" isn't a kind of operator (sine, saw, pulse, noise, level, string, tube, modes)", element["kind"]!.Span);
			foreach (var m in element["modulatedBy"]?.Items ?? [])
				op.Modulators.Add((OpNamed(m["op"], $"'{op.Name}' is modulated by"), m["index"]?.Num ?? "0"));
			foreach (var d in element["drivenBy"]?.Items ?? [])
				op.Drivers.Add(OpNamed(d, $"'{op.Name}' is driven by"));
			if (index.ContainsKey(op.Name))
				throw new JsonError($"two operators are named '{op.Name}'", element["name"]?.Span ?? element.Span);
			index[op.Name] = ops.Count;
			ops.Add(op);
		}
		var vars = Names(ops);
		var taps = (root["out"]?.Items ?? []).Select(t => (Op: OpNamed(t["op"], "out"), Level: t["level"]?.Num, Pan: t["pan"]?.Num)).ToList();
		var macros = (root["macros"]?.Items ?? []).Select(m => (
			Macro: m["macro"] ?? throw new JsonError("a macro names its \"macro\"", m.Span),
			Target: m["target"]?.Str ?? "filterCutoff",
			From: m["from"]?.Num ?? "0",
			To: m["to"]?.Num ?? "1",
			Op: m["op"] is { } op ? OpNamed(op, "a macro") : -1)).ToList();

		// As PatchCode: every operator up to the last that another reads (or that isn't heard) gets a variable; those
		// after it are made inline in Out, if Out lists them in their own order
		var referenced = new bool[ops.Count];
		foreach (var op in ops)
		{
			foreach (var m in op.Modulators)
				referenced[m.Op] = true;
			foreach (var d in op.Drivers)
				referenced[d] = true;
		}
		foreach (var macro in macros)
			if (macro.Op >= 0)
				referenced[macro.Op] = true;
		var heard = new bool[ops.Count];
		foreach (var tap in taps)
			heard[tap.Op] = true;
		var lastVar = -1;
		for (var i = 0; i < ops.Count; i++)
			if (referenced[i] || !heard[i])
				lastVar = i;
		var declared = new bool[ops.Count];
		for (var i = 0; i <= lastVar; i++)
			declared[i] = true;
		var inline = Enumerable.Range(0, ops.Count).Where(i => !declared[i]).ToArray();
		var outOrder = taps.Select(t => t.Op).Where(i => !declared[i]).ToArray();
		if (!inline.SequenceEqual(outOrder))
			for (var i = 0; i < declared.Length; i++)
				declared[i] = true;

		var body = new List<string>();
		for (var i = 0; i < ops.Count; i++)
			if (declared[i])
				body.Add($"var {vars[i]} = {Expression(ops[i], vars, root)};");
		var heardText = taps.Select(tap =>
		{
			var made = declared[tap.Op] ? vars[tap.Op] : Expression(ops[tap.Op], vars, root);
			var level = tap.Level ?? "1";
			return tap.Level is null && tap.Pan is null ? made : $"{made}.At({Code.F(level)}{(tap.Pan is { } pan ? ", " + Code.F(pan) : "")})";
		}).ToArray();
		if (heardText.Length > 0)
		{
			var one = $"p.Out({string.Join(", ", heardText)});";
			body.Add(one.Length <= 110 ? one : $"p.Out(\n    {string.Join(",\n    ", heardText)});");
		}
		if (root["filter"] is JsonObj filter)
			body.Add($"p.Filter({FilterCode(filter, root)});");
		if (root["pitchDrop"] is JsonObj drop)
			body.Add($"p.PitchDrop({Code.F(drop["semitones"]?.Num ?? "0")}, {Code.F(drop["seconds"]?.Num ?? "0")});");
		if (root["vibrato"] is JsonObj vibrato)
			body.Add($"p.Vibrato({Code.F(vibrato["rate"]?.Num ?? "0")}, {Code.F(vibrato["depth"]?.Num ?? "0")}{(vibrato["delay"]?.Num is { } delay ? ", " + Code.F(delay) : "")});");
		if (root["tremolo"] is JsonObj tremolo)
			body.Add($"p.Tremolo({Code.F(tremolo["rate"]?.Num ?? "0")}, {Code.F(tremolo["depth"]?.Num ?? "0")});");
		if (root["drive"]?.Num is { } drive)
			body.Add($"p.Drive({Code.F(drive)});");
		if (root["mono"] is JsonObj mono)
			body.Add(mono["glide"]?.Num is { } glide ? $"p.Mono({Code.F(glide)});" : "p.Mono();");
		if (root["sends"] is JsonObj sends)
		{
			if (sends["space"]?.Num is { } space)
				body.Add($"p.Send(ChipSend.Space, {Code.F(space)});");
			if (sends["echo"]?.Num is { } echo)
				body.Add($"p.Send(ChipSend.Echo, {Code.F(echo)});");
			if (sends["key"]?.Num is { } key)
				body.Add($"p.Send(ChipSend.Key, {Code.F(key)});");
		}
		if (root["gain"]?.Num is { } gain)
			body.Add($"p.Gain({Code.F(gain)});");
		if (root["pan"]?.Num is { } pan)
			body.Add($"p.Pan({Code.F(pan)});");
		foreach (var macro in macros)
		{
			var op = macro.Op >= 0 ? ", " + vars[macro.Op] : "";
			body.Add($"p.Macro({context.Macro(macro.Macro)}, MacroTarget.{Code.Pascal(macro.Target)}, {Code.F(macro.From)}, {Code.F(macro.To)}{op});");
		}

		lines.Add($"{indent}public static Patch {property} => Patch.Build({Code.Quote(name)}, p =>");
		lines.Add($"{indent}{{");
		foreach (var line in body)
			foreach (var part in line.Split('\n'))
				lines.Add($"{indent}    {part}");
		lines.Add($"{indent}}});");
		return lines;
	}

	private static Json Need(Json root, string key) => root[key] ?? throw new JsonError($"a patch file has a \"{key}\"", root.Span);

	// The variable each operator is written with, as PatchCode.OperatorNames names them
	private static string[] Names(List<Op> ops)
	{
		var names = new string[ops.Count];
		var used = new HashSet<string>();
		var counts = new Dictionary<string, int>();
		for (var i = 0; i < ops.Count; i++)
		{
			var name = Code.Identifier(ops[i].Name);
			if (name.Length > 0 && (Keywords.Contains(name) || name == "p"))
				name += "Op";
			if (name.Length == 0)
			{
				var kind = ops[i].Kind;
				counts[kind] = (counts.TryGetValue(kind, out var n) ? n : 0) + 1;
				name = kind + counts[kind];
			}
			var unique = name;
			for (var n = 2; !used.Add(unique); n++)
				unique = name + n;
			names[i] = unique;
		}
		return names;
	}

	private static readonly HashSet<string> Keywords =
	[
		"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
		"decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
		"fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long",
		"namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
		"readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct",
		"switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
		"virtual", "void", "volatile", "while",
	];

	/// <summary>An envelope as the file says it, as C#.</summary>
	public static string Envelope(string text, Json where)
	{
		var w = text.Split([' '], StringSplitOptions.RemoveEmptyEntries);
		if (w.Length == 1 && w[0] == "gate")
			return "Env.Gate";
		if (w.Length == 2 && w[0] == "decay")
			return $"Env.Decay({Code.F(w[1])})";
		if (w.Length == 5 && w[0] == "adsr")
			return $"Env.Adsr({Code.F(w[1])}, {Code.F(w[2])}, {Code.F(w[3])}, {Code.F(w[4])})";
		throw new JsonError($"\"{text}\" isn't an envelope (\"gate\", \"decay s\" or \"adsr a d s r\")", where.Span);
	}

	private static string LevelArgs(Op o, bool force)
	{
		var velocity = o.Velocity is { } v && Code.Value(v) != 0f;
		var text = force || Code.Value(o.Level) != 1f || velocity ? ", " + Code.F(o.Level) : "";
		return velocity ? text + $", velocity: {Code.F(o.Velocity!)}" : text;
	}

	private static string Expression(Op o, string[] vars, Json root)
	{
		var env = Envelope(o.Envelope, root);
		var pulseWidth = o.Kind == "pulse" && Code.Value(o.PulseWidth) != 0.5f;
		var text = o.Kind switch
		{
			"sine" => $"p.Sine({Code.F(o.Ratio)}, {env}{LevelArgs(o, false)})",
			"saw" or "pulse" => $"p.Wave(Waveform.{Code.Pascal(o.Kind)}, {Code.F(o.Ratio)}, {env}{LevelArgs(o, pulseWidth)}{(pulseWidth ? $", pulseWidth: {Code.F(o.PulseWidth)}" : "")})",
			"noise" => $"p.Noise({env}{LevelArgs(o, o.Pink)}{(o.Pink ? ", pink: true" : "")})",
			"level" => $"p.Level({env}{LevelArgs(o, false)})",
			"string" => $"p.String({Code.F(o.Ratio)}, decaySeconds: {Code.F(o.DecaySeconds)}, damping: {Code.F(o.Damping)}, pick: {Code.F(o.Pick)})",
			"tube" => Code.Value(o.Ratio) != 1f ? $"p.Tube({Code.F(o.Ratio)})" : "p.Tube()",
			_ => $"p.Modes({string.Join(", ", o.Modes.Select(m => ModeCode(m, root)))})",
		};
		if (o.Fixed is { } hz && o.Kind != "modes")
			text += $".Fixed({Code.F(hz)})";
		if (o.Detune is { } detune)
			text += $".Detune({Code.F(detune)})";
		if (o.Phase is { } phase)
			text += $".Phase({Code.F(phase)})";
		if (o.Feedback is { } feedback)
			text += $".Feedback({Code.F(feedback)})";
		foreach (var (op, index) in o.Modulators)
			text += $".ModulatedBy({vars[op]}, {Code.F(index)})";
		if (o.Drivers.Count > 0)
			text += $".DrivenBy({string.Join(", ", o.Drivers.Select(d => vars[d]))})";
		if (o.KeyLevel is { } keyLevel)
			text += $".KeyLevel({Code.F(keyLevel)})";
		if (o.KeyDecay is { } keyDecay)
			text += $".KeyDecay({Code.F(keyDecay)})";
		return text;
	}

	private static string ModeCode(string mode, Json root)
	{
		var words = mode.Split([' '], StringSplitOptions.RemoveEmptyEntries);
		if (words.Length is < 3 or > 4 || words[0] is not ("at" or "fixed"))
			throw new JsonError($"\"{mode}\" isn't a mode (\"at ratio seconds [level]\" or \"fixed hz seconds [level]\")", root.Span);
		var level = words.Length == 4 ? ", " + Code.F(words[3]) : "";
		return $"Mode.{(words[0] == "at" ? "At" : "Fixed")}({Code.F(words[1])}, {Code.F(words[2])}{level})";
	}

	private static string FilterCode(JsonObj f, Json root)
	{
		var text = new StringBuilder($"Filter.{Code.Pascal(f["kind"]?.Str ?? "lowPass")}({Code.F(f["cutoff"]?.Num ?? throw new JsonError("a filter has a \"cutoff\"", f.Span))}");
		if (f["q"]?.Num is { } q)
			text.Append($", q: {Code.F(q)}");
		if (f["keyTrack"]?.Num is { } keyTrack)
			text.Append($", keyTrack: {Code.F(keyTrack)}");
		if (f["velocity"]?.Num is { } velocity)
			text.Append($", velocity: {Code.F(velocity)}");
		if (f["envelopeOctaves"]?.Num is { } octaves)
			text.Append($", envelopeOctaves: {Code.F(octaves)}, envelope: {Envelope(f["envelope"]?.Str ?? "gate", root)}");
		return text.Append(')').ToString();
	}
}
