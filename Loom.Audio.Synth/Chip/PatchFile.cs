using System.Globalization;
using System.Text;
using System.Text.Json;

using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Chip;

/// <summary>
/// A patch as a file, <c>loom-patch/1</c>: everything a <see cref="PatchSpec"/> holds as JSON, each operator named and
/// routed by name, and only what differs from a new spec's defaults written, so a patch reads back exactly as it was.
/// Loom's music generator turns the same file into the C# <see cref="PatchCode"/> writes.
/// <code>
/// {
///   "format": "loom-patch/1",
///   "name": "electric piano",
///   "operators": [
///     { "name": "tine", "kind": "sine", "ratio": 14, "envelope": "decay 0.12", "level": 0.6, "velocity": 0.8 },
///     { "name": "body", "kind": "sine", "envelope": "adsr 0.002 1.6 0.2 0.3", "modulatedBy": [{ "op": "tine", "index": 1.2 }] }
///   ],
///   "out": [{ "op": "body" }],
///   "sends": { "space": 0.2 }
/// }
/// </code>
/// An envelope is <c>"gate"</c>, <c>"decay seconds"</c> (<see cref="Env.Decay"/>) or <c>"adsr a d s r"</c>; a mode is
/// <c>"at ratio seconds [level]"</c> or <c>"fixed hz seconds [level]"</c>. A file whose patch is code (<c>"code":
/// "Name"</c>, ejected) has no patch to read.
/// </summary>
public static class PatchFile
{
	/// <summary>The format this writes, and the only one it reads.</summary>
	public const string Format = "loom-patch/1";

	private static readonly OperatorSpec Defaults = new();

	/// <summary>The file for <paramref name="spec"/>.</summary>
	/// <param name="spec">The patch.</param>
	/// <param name="names">Each operator's name, by index; by kind ("sine1") where not given (<see cref="PatchCode.OperatorNames"/>).</param>
	/// <exception cref="NotSupportedException">An operator plays samples, which a file can't hold.</exception>
	public static string Write(PatchSpec spec, IReadOnlyList<string>? names = null)
	{
		ArgumentNullException.ThrowIfNull(spec);
		var ops = spec.Operators;
		var vars = PatchCode.OperatorNames(ops, names);
		using var stream = new MemoryStream();
		using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			json.WriteStartObject();
			json.WriteString("format", Format);
			json.WriteString("name", spec.Name);
			json.WriteStartArray("operators");
			for (var i = 0; i < ops.Count; i++)
				WriteOperator(json, ops[i], vars, vars[i]);
			json.WriteEndArray();
			json.WriteStartArray("out");
			foreach (var tap in spec.Outputs)
			{
				json.WriteStartObject();
				json.WriteString("op", vars[tap.Operator]);
				if (tap.Level != 1f)
					json.WriteNumber("level", tap.Level);
				if (tap.Pan != 0f)
					json.WriteNumber("pan", tap.Pan);
				json.WriteEndObject();
			}
			json.WriteEndArray();
			if (spec.Filter is { } filter)
			{
				json.WriteStartObject("filter");
				json.WriteString("kind", Camel(filter.Kind.ToString()));
				json.WriteNumber("cutoff", filter.CutoffHz);
				if (filter.Q != 0.7f)
					json.WriteNumber("q", filter.Q);
				if (filter.KeyTrack != 0f)
					json.WriteNumber("keyTrack", filter.KeyTrack);
				if (filter.Velocity != 0f)
					json.WriteNumber("velocity", filter.Velocity);
				if (filter.EnvelopeOctaves != 0f || filter.Envelope != default)
				{
					json.WriteNumber("envelopeOctaves", filter.EnvelopeOctaves);
					json.WriteString("envelope", Envelope(filter.Envelope));
				}
				json.WriteEndObject();
			}
			Number(json, "drive", spec.Drive);
			Number(json, "gain", spec.GainDb);
			Number(json, "pan", spec.Pan);
			if (spec.SendSpace != 0f || spec.SendEcho != 0f || spec.SendKey != 0f)
			{
				json.WriteStartObject("sends");
				Number(json, "space", spec.SendSpace);
				Number(json, "echo", spec.SendEcho);
				Number(json, "key", spec.SendKey);
				json.WriteEndObject();
			}
			if (spec.VibratoHz != 0f || spec.VibratoSemitones != 0f || spec.VibratoDelaySeconds != 0f)
			{
				json.WriteStartObject("vibrato");
				json.WriteNumber("rate", spec.VibratoHz);
				json.WriteNumber("depth", spec.VibratoSemitones);
				Number(json, "delay", spec.VibratoDelaySeconds);
				json.WriteEndObject();
			}
			if (spec.TremoloHz != 0f || spec.TremoloDepth != 0f)
			{
				json.WriteStartObject("tremolo");
				json.WriteNumber("rate", spec.TremoloHz);
				json.WriteNumber("depth", spec.TremoloDepth);
				json.WriteEndObject();
			}
			if (spec.PitchDropSemitones != 0f || spec.PitchDropSeconds != 0f)
			{
				json.WriteStartObject("pitchDrop");
				json.WriteNumber("semitones", spec.PitchDropSemitones);
				json.WriteNumber("seconds", spec.PitchDropSeconds);
				json.WriteEndObject();
			}
			if (spec.Mono)
			{
				json.WriteStartObject("mono");
				Number(json, "glide", spec.GlideSeconds);
				json.WriteEndObject();
			}
			if (spec.Macros.Count > 0)
			{
				json.WriteStartArray("macros");
				foreach (var macro in spec.Macros)
				{
					json.WriteStartObject();
					json.WriteNumber("macro", macro.Macro.Id);
					json.WriteString("target", Camel(macro.Target.ToString()));
					json.WriteNumber("from", macro.From);
					json.WriteNumber("to", macro.To);
					if (macro.Operator >= 0)
						json.WriteString("op", vars[macro.Operator]);
					json.WriteEndObject();
				}
				json.WriteEndArray();
			}
			json.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
	}

	private static void WriteOperator(Utf8JsonWriter json, OperatorSpec op, string[] vars, string name)
	{
		if (op.Kind == OperatorKind.Sample)
			throw new NotSupportedException($"Operator '{name}' plays samples, which a patch file can't hold: keep the patch as code.");
		json.WriteStartObject();
		json.WriteString("name", name);
		json.WriteString("kind", op.Kind == OperatorKind.Wave ? Camel(op.Waveform.ToString()) : Camel(op.Kind.ToString()));
		if (op.Ratio != Defaults.Ratio)
			json.WriteNumber("ratio", op.Ratio);
		Number(json, "fixed", op.FixedHz);
		Number(json, "detune", op.DetuneCents);
		Number(json, "phase", op.StartPhase);
		if (op.Envelope != Defaults.Envelope)
			json.WriteString("envelope", Envelope(op.Envelope));
		if (op.Level != Defaults.Level)
			json.WriteNumber("level", op.Level);
		Number(json, "velocity", op.Velocity);
		Number(json, "feedback", op.Feedback);
		if (op.PulseWidth != Defaults.PulseWidth)
			json.WriteNumber("pulseWidth", op.PulseWidth);
		if (op.Pink)
			json.WriteBoolean("pink", true);
		if (op.OneShot)
			json.WriteBoolean("oneShot", true);
		if (op.DecaySeconds != Defaults.DecaySeconds)
			json.WriteNumber("decaySeconds", op.DecaySeconds);
		if (op.Damping != Defaults.Damping)
			json.WriteNumber("damping", op.Damping);
		if (op.Pick != Defaults.Pick)
			json.WriteNumber("pick", op.Pick);
		if (op.Modes.Length > 0)
		{
			json.WriteStartArray("modes");
			foreach (var mode in op.Modes)
				json.WriteStringValue(ModeText(mode));
			json.WriteEndArray();
		}
		if (op.Modulators.Length > 0)
		{
			json.WriteStartArray("modulatedBy");
			foreach (var m in op.Modulators)
			{
				json.WriteStartObject();
				json.WriteString("op", vars[m.Operator]);
				json.WriteNumber("index", m.Index);
				json.WriteEndObject();
			}
			json.WriteEndArray();
		}
		if (op.Drivers.Length > 0)
		{
			json.WriteStartArray("drivenBy");
			foreach (var d in op.Drivers)
				json.WriteStringValue(vars[d]);
			json.WriteEndArray();
		}
		Number(json, "keyLevel", op.KeyLevelDb);
		Number(json, "keyDecay", op.KeyDecay);
		json.WriteEndObject();
	}

	/// <summary>The patch a file holds.</summary>
	/// <exception cref="FormatException">It isn't a <c>loom-patch/1</c> file, or holds something this can't read.</exception>
	/// <exception cref="PatchException">What it holds breaks a patch's rules.</exception>
	public static Patch Load(string json) => Read(json).ToPatch();

	/// <summary>What a file holds, as a spec to change or build.</summary>
	/// <exception cref="FormatException">It isn't a <c>loom-patch/1</c> file, or holds something this can't read.</exception>
	public static PatchSpec Read(string json) => Read(json, out _);

	/// <inheritdoc cref="Read(string)"/>
	/// <param name="json">The file's text.</param>
	/// <param name="names">Each operator's name, as the file gives it.</param>
	public static PatchSpec Read(string json, out IReadOnlyList<string> names)
	{
		ArgumentNullException.ThrowIfNull(json);
		using var document = Parse(json);
		var root = document.RootElement;
		var format = String(root, "format");
		if (format != Format)
			throw new FormatException($"A patch file says \"format\": \"{Format}\"; this one says \"{format}\".");
		var name = String(root, "name") ?? throw new FormatException("A patch file has a \"name\".");
		if (String(root, "code") is { } code)
			throw new FormatException($"Patch '{name}' is code ({code}), not data: build it from its C#.");
		var spec = new PatchSpec { Name = name };
		var given = new List<string>();
		var index = new Dictionary<string, int>(StringComparer.Ordinal);
		int Op(string? opName, string where) =>
			opName is not null && index.TryGetValue(opName, out var i)
				? i
				: throw new FormatException($"Patch '{name}': {where} names operator '{opName}', which no operator before it is.");
		foreach (var element in Array(root, "operators"))
		{
			var opName = String(element, "name") ?? $"op{given.Count + 1}";
			var spec1 = ReadOperator(element, name, opName) with
			{
				Modulators = [.. Array(element, "modulatedBy").Select(m => new Modulation(Op(String(m, "op"), $"'{opName}' is modulated by"), Float(m, "index") ?? 0f))],
				Drivers = [.. Array(element, "drivenBy").Select(d => Op(d.GetString(), $"'{opName}' is driven by"))],
			};
			if (!index.TryAdd(opName, given.Count))
				throw new FormatException($"Patch '{name}' has two operators named '{opName}'.");
			given.Add(opName);
			spec.Operators.Add(spec1);
		}
		foreach (var tap in Array(root, "out"))
			spec.Outputs.Add(new OutputTap(Op(String(tap, "op"), "out"), Float(tap, "level") ?? 1f, Float(tap, "pan") ?? 0f));
		if (Object(root, "filter") is { } filter)
		{
			var kind = Enum<FilterKind>(String(filter, "kind"), name, "filter kind");
			spec.Filter = new FilterSpec(kind, Float(filter, "cutoff") ?? throw new FormatException($"Patch '{name}': the filter has a \"cutoff\"."),
				Float(filter, "q") ?? 0.7f, Float(filter, "keyTrack") ?? 0f, Float(filter, "velocity") ?? 0f,
				Float(filter, "envelopeOctaves") ?? 0f, String(filter, "envelope") is { } envelope ? Envelope(envelope, name) : default);
		}
		spec.Drive = Float(root, "drive") ?? 0f;
		spec.GainDb = Float(root, "gain") ?? 0f;
		spec.Pan = Float(root, "pan") ?? 0f;
		if (Object(root, "sends") is { } sends)
		{
			spec.SendSpace = Float(sends, "space") ?? 0f;
			spec.SendEcho = Float(sends, "echo") ?? 0f;
			spec.SendKey = Float(sends, "key") ?? 0f;
		}
		if (Object(root, "vibrato") is { } vibrato)
		{
			spec.VibratoHz = Float(vibrato, "rate") ?? 0f;
			spec.VibratoSemitones = Float(vibrato, "depth") ?? 0f;
			spec.VibratoDelaySeconds = Float(vibrato, "delay") ?? 0f;
		}
		if (Object(root, "tremolo") is { } tremolo)
		{
			spec.TremoloHz = Float(tremolo, "rate") ?? 0f;
			spec.TremoloDepth = Float(tremolo, "depth") ?? 0f;
		}
		if (Object(root, "pitchDrop") is { } drop)
		{
			spec.PitchDropSemitones = Float(drop, "semitones") ?? 0f;
			spec.PitchDropSeconds = Float(drop, "seconds") ?? 0f;
		}
		if (Object(root, "mono") is { } mono)
		{
			spec.Mono = true;
			spec.GlideSeconds = Float(mono, "glide") ?? 0f;
		}
		foreach (var macro in Array(root, "macros"))
			spec.Macros.Add(new MacroSpec(new Macro(macro.TryGetProperty("macro", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32()
					: throw new FormatException($"Patch '{name}': a macro is named by its number (\"macro\": 0).")),
				Enum<MacroTarget>(String(macro, "target"), name, "macro target"), Float(macro, "from") ?? 0f, Float(macro, "to") ?? 1f,
				String(macro, "op") is { } op ? Op(op, "a macro") : -1));
		names = given;
		return spec;
	}

	private static OperatorSpec ReadOperator(JsonElement element, string patch, string name)
	{
		var kindText = String(element, "kind") ?? throw new FormatException($"Patch '{patch}': operator '{name}' has a \"kind\".");
		var (kind, waveform) = kindText switch
		{
			"saw" => (OperatorKind.Wave, Waveform.Saw),
			"pulse" => (OperatorKind.Wave, Waveform.Pulse),
			"sample" => throw new FormatException($"Patch '{patch}': operator '{name}' plays samples, which a patch file can't hold."),
			_ => (Enum<OperatorKind>(kindText, patch, "operator kind"), default),
		};
		return new OperatorSpec
		{
			Kind = kind,
			Waveform = waveform,
			Ratio = Float(element, "ratio") ?? Defaults.Ratio,
			FixedHz = Float(element, "fixed") ?? 0f,
			DetuneCents = Float(element, "detune") ?? 0f,
			StartPhase = Float(element, "phase") ?? 0f,
			Envelope = String(element, "envelope") is { } envelope ? Envelope(envelope, patch) : Defaults.Envelope,
			Level = Float(element, "level") ?? Defaults.Level,
			Velocity = Float(element, "velocity") ?? 0f,
			Feedback = Float(element, "feedback") ?? 0f,
			PulseWidth = Float(element, "pulseWidth") ?? Defaults.PulseWidth,
			Pink = Bool(element, "pink"),
			OneShot = Bool(element, "oneShot"),
			DecaySeconds = Float(element, "decaySeconds") ?? Defaults.DecaySeconds,
			Damping = Float(element, "damping") ?? Defaults.Damping,
			Pick = Float(element, "pick") ?? Defaults.Pick,
			Modes = [.. Array(element, "modes").Select(m => Mode(m.GetString() ?? "", patch))],
			KeyLevelDb = Float(element, "keyLevel") ?? 0f,
			KeyDecay = Float(element, "keyDecay") ?? 0f,
		};
	}

	/// <summary>An envelope as a file writes it: "gate", "decay 0.3" or "adsr 0.01 0.4 0.7 0.15".</summary>
	public static string Envelope(EnvelopeShape e) =>
		e == EnvelopeShape.Gate ? "gate"
		: e == EnvelopeShape.Struck(e.Decay) ? $"decay {F(e.Decay)}"
		: $"adsr {F(e.Attack)} {F(e.Decay)} {F(e.Sustain)} {F(e.Release)}";

	/// <summary>An envelope as a file writes it, read.</summary>
	/// <exception cref="FormatException">It isn't one.</exception>
	public static EnvelopeShape Envelope(string text) => Envelope(text, "");

	private static EnvelopeShape Envelope(string text, string patch)
	{
		var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return words switch
		{
			["gate"] => EnvelopeShape.Gate,
			["decay", var s] => EnvelopeShape.Struck(Number(s, patch)),
			["adsr", var a, var d, var s, var r] => EnvelopeShape.Adsr(Number(a, patch), Number(d, patch), Number(s, patch), Number(r, patch)),
			_ => throw new FormatException($"Patch '{patch}': \"{text}\" isn't an envelope (\"gate\", \"decay s\" or \"adsr a d s r\")."),
		};
	}

	private static string ModeText(Mode m) =>
		(m.Hz > 0f ? $"fixed {F(m.Hz)} {F(m.DecaySeconds)}" : $"at {F(m.Ratio)} {F(m.DecaySeconds)}") + (m.Level != 1f ? $" {F(m.Level)}" : "");

	private static Mode Mode(string text, string patch)
	{
		var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var level = words.Length == 4 ? Number(words[3], patch) : 1f;
		return words switch
		{
			["at", var r, var s, ..] when words.Length <= 4 => Chip.Mode.At(Number(r, patch), Number(s, patch), level),
			["fixed", var hz, var s, ..] when words.Length <= 4 => Chip.Mode.Fixed(Number(hz, patch), Number(s, patch), level),
			_ => throw new FormatException($"Patch '{patch}': \"{text}\" isn't a mode (\"at ratio seconds [level]\" or \"fixed hz seconds [level]\")."),
		};
	}

	private static string F(float value) => value.ToString(CultureInfo.InvariantCulture);

	private static float Number(string text, string patch) =>
		float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
			? value
			: throw new FormatException($"Patch '{patch}': \"{text}\" isn't a number.");

	private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

	private static T Enum<T>(string? text, string patch, string what) where T : struct, System.Enum =>
		System.Enum.TryParse<T>(text, ignoreCase: true, out var value) && !int.TryParse(text, out _)
			? value
			: throw new FormatException($"Patch '{patch}': \"{text}\" isn't a {what} ({string.Join(", ", System.Enum.GetNames<T>().Select(Camel))}).");

	private static void Number(Utf8JsonWriter json, string name, float value)
	{
		if (value != 0f)
			json.WriteNumber(name, value);
	}

	internal static JsonDocument Parse(string json)
	{
		try
		{
			return JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
		}
		catch (JsonException ex)
		{
			throw new FormatException($"Not JSON: {ex.Message}", ex);
		}
	}

	internal static string? String(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	internal static float? Float(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : null;

	internal static bool Bool(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

	internal static JsonElement? Object(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

	internal static IEnumerable<JsonElement> Array(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
}
