using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// A song as a file, <c>loom-song/1</c>: everything a <see cref="Song"/> is, its patches, kits, cues and macros named,
/// written a part to a line. Loom's music generator turns the same file into the C# a person would have written; at run
/// time <see cref="Load"/> builds it with the names bound by a <see cref="SongBindings"/>.
/// <code>
/// {
///   "format": "loom-song/1",
///   "name": "open-road",
///   "tempo": 108,
///   "scale": "G major",
///   "groups": [{ "name": "wide", "effects": [{ "fx": "chorus", "mix": 0.4 }] }],
///   "tracks": [
///     { "name": "drums", "kit": "general", "level": -9, "humanise": { "ms": 6, "velocity": 0.1 } },
///     { "name": "keys", "patch": "electric-piano", "group": "wide", "level": -5 }
///   ],
///   "sections": [
///     {
///       "name": "cruise", "bars": 8, "chords": "I | I | IV | I",
///       "parts": [
///         { "track": "keys", "line": "eighths", "notes": "5 - 3 5 6 - 5 3", "onPhrase": true, "name": "the tune" },
///         { "track": "drums", "beat": "eighths", "rows": { "kick": "x... x.x.", "snare": "..x. ..xo" }, "when": "intensity > 0.25" }
///       ]
///     }
///   ],
///   "conduct": [{ "when": "intensity > 0.7", "then": "open" }, { "then": "cruise" }]
/// }
/// </code>
/// A track's defaults (octave 4, level 0, no pan, no looseness, straight time) and a song's (4/4, 32 voices) are left
/// out. A part is a line (<c>"line": "eighths", "notes"</c>, <c>"overChords"</c>, <c>"octave"</c>), a beat
/// (<c>"beat"</c>, <c>"rows"</c>), a strum (<c>"strum"</c>, <c>"notes"</c>, <c>"voicing"</c>, <c>"spread"</c>) or code
/// (<c>"part": { "code": "Name" }</c>); a whole placement can be code (<c>"code": "Name"</c>). Conditions are
/// <see cref="ConditionText"/>'s. The sections play in <c>"order"</c>, or as <c>"conduct"</c>'s rules choose, the first
/// whose condition holds; <c>"hooks": ["OnBuilt"]</c> hands the built song to code of the game's own.
/// </summary>
public static class SongFile
{
	/// <summary>The format this writes, and the only one it reads.</summary>
	public const string Format = "loom-song/1";

	private static readonly (string Name, Func<Note, Scale> Make)[] Scales =
	[
		("major", Scale.Major), ("minor", Scale.Minor), ("dorian", Scale.Dorian), ("phrygian", Scale.Phrygian),
		("lydian", Scale.Lydian), ("mixolydian", Scale.Mixolydian), ("harmonic minor", Scale.HarmonicMinor),
		("pentatonic major", Scale.PentatonicMajor), ("pentatonic minor", Scale.PentatonicMinor), ("blues", Scale.Blues),
	];

	private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

	/// <summary>The file for <paramref name="song"/>, its references named by <paramref name="names"/>.</summary>
	/// <exception cref="NotSupportedException">The song holds something a file can't say: a patch the catalog doesn't name.</exception>
	public static string Write(Song song, SongNames? names = null) => Write(ToJson(song, names));

	/// <summary>A file's JSON as the music files are written: a part to a line.</summary>
	public static string Write(JsonObject file)
	{
		ArgumentNullException.ThrowIfNull(file);
		return JsonText.Write(file);
	}

	/// <summary>
	/// The file for <paramref name="song"/> as JSON, to add to before it is written: a conductor (<c>"conduct"</c>), which
	/// a built song can't say, or hooks.
	/// </summary>
	/// <exception cref="NotSupportedException">The song holds something a file can't say.</exception>
	public static JsonObject ToJson(Song song, SongNames? names = null)
	{
		ArgumentNullException.ThrowIfNull(song);
		names ??= new SongNames();
		var file = new JsonObject
		{
			["format"] = Format,
			["name"] = song.Name,
			["tempo"] = song.Tempo,
		};
		if (song.Meter != Meter.FourFour)
			file["meter"] = song.Meter.ToString();
		file["scale"] = ScaleJson(song.Scale);
		if (song.Voices != 32)
			file["voices"] = song.Voices;
		if (song.Groups.Count > 0)
			file["groups"] = new JsonArray([.. song.Groups.Select(g => new JsonObject { ["name"] = g.Name, ["effects"] = names.GroupEffects(g) ?? [] })]);
		file["tracks"] = new JsonArray([.. song.Tracks.Select(t => TrackJson(t, names))]);
		file["sections"] = new JsonArray([.. song.Sections.Select(s => SectionJson(s, names))]);
		if (song.OrderList.Count > 0)
			file["order"] = new JsonArray([.. song.OrderList.Select(s => (JsonNode)s.Name)]);
		return file;
	}

	private static JsonNode ScaleJson(Scale scale) =>
		Array.Exists(Scales, s => s.Name == scale.Name && s.Make(scale.Root).Steps.SequenceEqual(scale.Steps))
			? $"{NoteNames[(int)scale.Root]} {scale.Name}"
			: new JsonObject { ["root"] = NoteNames[(int)scale.Root], ["name"] = scale.Name, ["steps"] = new JsonArray([.. scale.Steps.Select(s => (JsonNode)s)]) };

	private static JsonObject TrackJson(SongTrack track, SongNames names)
	{
		var json = new JsonObject { ["name"] = track.Name };
		if (track.Patch is { } patch)
			json["patch"] = names.Patch(patch) ?? throw new NotSupportedException($"Track '{track.Name}' plays a patch ('{patch.Name}') the catalog doesn't name.");
		else
			json["kit"] = names.Kit(track.Kit!) ?? throw new NotSupportedException($"Track '{track.Name}' plays a kit ('{track.Kit!.Name}') the catalog doesn't name.");
		if (track.Group is { } group)
			json["group"] = group.Name;
		if (track.OctaveValue != 4)
			json["octave"] = track.OctaveValue;
		if (track.GainDb != 0f)
			json["level"] = track.GainDb;
		if (track.PanValue != 0f)
			json["pan"] = track.PanValue;
		if (track.HumaniseMs != 0f || track.HumaniseVelocity != 0f)
			json["humanise"] = new JsonObject { ["ms"] = track.HumaniseMs, ["velocity"] = track.HumaniseVelocity };
		if (track.SwingRatio != 0.5f || track.SwingStep != Ticks.Eighth)
			json["swing"] = new JsonObject { ["ratio"] = track.SwingRatio, ["step"] = StepName(track.SwingStep) };
		if (track.Macros.Count > 0)
			json["macros"] = new JsonArray([.. track.Macros.Select(m =>
			{
				var macro = new JsonObject { ["macro"] = MacroJson(m.Macro, names) };
				if (m.Cue is { } cue)
				{
					macro["follow"] = cue.Name;
					macro["from"] = m.From;
					macro["to"] = m.To;
				}
				else
					macro["set"] = m.From;
				return macro;
			})]);
		return json;
	}

	private static JsonNode MacroJson(Macro macro, SongNames names) => names.Macro(macro) is { } name ? name : macro.Id;

	private static JsonObject SectionJson(Section section, SongNames names)
	{
		var json = new JsonObject
		{
			["name"] = section.Name,
			["bars"] = section.Bars,
			["chords"] = section.ChordText,
			["parts"] = new JsonArray([.. section.Placements.Select(ToJson)]),
		};
		if (section.Sweeps.Count > 0)
			json["sweeps"] = new JsonArray([.. section.Sweeps.Select(s => new JsonObject
			{
				["track"] = s.Track.Name,
				["macro"] = MacroJson(s.Macro, names),
				["from"] = s.From,
				["to"] = s.To,
			})]);
		return json;
	}

	/// <summary>
	/// A placement as a file writes it among a section's parts: its track, name, part, condition,
	/// level, phrase and fill.
	/// </summary>
	public static JsonObject ToJson(Placement placement)
	{
		var json = new JsonObject { ["track"] = placement.Track.Name };
		if (placement.Name != placement.Track.Name)
			json["name"] = placement.Name;
		foreach (var (key, value) in ToJson(placement.Part))
			json[key] = value?.DeepClone();
		if (placement.Condition is { } condition)
			json["when"] = ConditionText.Write(condition);
		if (placement.LevelCue is { } level)
			json["level"] = level.Name;
		if (placement.IsOnPhrase)
			json["onPhrase"] = true;
		if (placement.FillPart is { } fill)
			json["endWith"] = ToJson(fill);
		return json;
	}

	/// <summary>
	/// A part as a file writes it: a line's, a beat's or a strum's keys, for a part brought back from
	/// code to data.
	/// </summary>
	public static JsonObject ToJson(Part part)
	{
		switch (part)
		{
			case Line line:
				var lineJson = new JsonObject { ["line"] = StepName(line.Step), ["notes"] = line.Text };
				if (line.IsOverChords)
					lineJson["overChords"] = true;
				if (line.OctaveShift != 0)
					lineJson["octave"] = line.OctaveShift;
				return lineJson;
			case Beat beat:
				var rows = new JsonObject();
				foreach (var (piece, pattern) in beat.Rows)
					if (!rows.TryAdd(piece.Id, pattern))
						throw new NotSupportedException($"A beat with two rows for '{piece.Id}', which a file can't say.");
				return new JsonObject { ["beat"] = StepName(beat.Step), ["rows"] = rows };
			case Strum strum:
				var strumJson = new JsonObject { ["strum"] = StepName(strum.Step), ["notes"] = strum.Text };
				if (strum.ChordVoicing != Voicing.Open)
					strumJson["voicing"] = EffectText.Camel(strum.ChordVoicing.ToString());
				if (strum.SpreadMs != 12f)
					strumJson["spread"] = strum.SpreadMs;
				return strumJson;
			default:
				throw new NotSupportedException($"A part a file can't say: {part.GetType().Name}.");
		}
	}

	private static string StepName(int step) => step switch
	{
		Ticks.Quarter => "quarters",
		Ticks.Eighth => "eighths",
		Ticks.Sixteenth => "sixteenths",
		Ticks.Triplet => "triplets",
		_ => throw new NotSupportedException($"A step of {step} ticks, which a file can't say."),
	};

	private static int Step(string? name, string song) => name switch
	{
		"quarters" => Ticks.Quarter,
		"eighths" => Ticks.Eighth,
		"sixteenths" => Ticks.Sixteenth,
		"triplets" => Ticks.Triplet,
		_ => throw new FormatException($"Song '{song}': \"{name}\" isn't a step (quarters, eighths, sixteenths or triplets)."),
	};

	/// <summary>The song a file holds, built with its names bound by <paramref name="bindings"/>.</summary>
	/// <exception cref="FormatException">It isn't a <c>loom-song/1</c> file, or names something the bindings don't have.</exception>
	/// <exception cref="NotationException">A part's notation has a mistake.</exception>
	public static Song Load(string json, SongBindings bindings)
	{
		ArgumentNullException.ThrowIfNull(json);
		ArgumentNullException.ThrowIfNull(bindings);
		using var document = PatchFile.Parse(json);
		var root = document.RootElement;
		var format = PatchFile.String(root, "format");
		if (format != Format)
			throw new FormatException($"A song file says \"format\": \"{format}\"; this reads \"{Format}\".");
		var name = PatchFile.String(root, "name") ?? throw new FormatException("A song file has a \"name\".");
		FormatException Error(string why) => new($"Song '{name}': {why}.");

		var song = new Song(name);
		if (PatchFile.Float(root, "tempo") is { } tempo)
			song.Tempo = tempo;
		if (PatchFile.String(root, "meter") is { } meter)
			song.Meter = ParseMeter(meter) ?? throw Error($"\"{meter}\" isn't a meter (\"3/4\")");
		if (root.TryGetProperty("scale", out var scale))
			song.Scale = ParseScale(scale) ?? throw Error($"{scale} isn't a scale (\"G major\")");
		if (root.TryGetProperty("voices", out var voices))
			song.Voices = voices.GetInt32();
		if (PatchFile.Object(root, "effects") is { } effects)
		{
			if (effects.TryGetProperty("space", out _))
				song.SpaceEffects = [.. PatchFile.Array(effects, "space").Select(e => EffectText.Make(e, name))];
			if (effects.TryGetProperty("echo", out _))
				song.EchoEffects = [.. PatchFile.Array(effects, "echo").Select(e => EffectText.Make(e, name))];
			if (effects.TryGetProperty("master", out _))
				song.MasterEffects = [.. PatchFile.Array(effects, "master").Select(e => EffectText.Make(e, name))];
		}

		var groups = new Dictionary<string, SongGroup>(StringComparer.Ordinal);
		foreach (var group in PatchFile.Array(root, "groups"))
		{
			var groupName = PatchFile.String(group, "name") ?? throw Error("a group has a \"name\"");
			groups[groupName] = song.Group(groupName, [.. PatchFile.Array(group, "effects").Select(e => EffectText.Make(e, name))]);
		}

		CueBase Cue(string cueName) => bindings.Cue(cueName) ?? throw Error($"no cue is named \"{cueName}\"");
		Macro MacroOf(JsonElement element) => element.ValueKind == JsonValueKind.Number
			? new Macro(element.GetInt32())
			: bindings.Macro(element.GetString() ?? "") ?? throw Error($"no macro is named \"{element}\"");
		var tracks = new Dictionary<string, SongTrack>(StringComparer.Ordinal);
		foreach (var element in PatchFile.Array(root, "tracks"))
		{
			var trackName = PatchFile.String(element, "name") ?? throw Error("a track has a \"name\"");
			SongGroup? group = PatchFile.String(element, "group") is { } groupName
				? groups.GetValueOrDefault(groupName) ?? throw Error($"track '{trackName}' is in group '{groupName}', which isn't one")
				: null;
			var track = PatchFile.String(element, "patch") is { } patchName
				? song.Track(trackName, bindings.Patch(patchName) ?? throw Error($"track '{trackName}' plays patch '{patchName}', which isn't one"), group)
				: PatchFile.String(element, "kit") is { } kitName
					? song.Track(trackName, bindings.Kit(kitName) ?? throw Error($"track '{trackName}' plays kit '{kitName}', which isn't one"), group)
					: throw Error($"track '{trackName}' plays a \"patch\" or a \"kit\"");
			if (!tracks.TryAdd(trackName, track))
				throw Error($"two tracks are named '{trackName}'");
			if (element.TryGetProperty("octave", out var octave))
				track.Octave(octave.GetInt32());
			if (PatchFile.Float(element, "level") is { } level)
				track.Level(level);
			if (PatchFile.Float(element, "pan") is { } pan)
				track.Pan(pan);
			if (PatchFile.Object(element, "humanise") is { } humanise)
				track.Humanise(PatchFile.Float(humanise, "ms") ?? 0f, PatchFile.Float(humanise, "velocity") ?? 0f);
			if (PatchFile.Object(element, "swing") is { } swing)
				track.Swing(PatchFile.Float(swing, "ratio") ?? 0.5f, PatchFile.String(swing, "step") is { } step ? Step(step, name) : Ticks.Eighth);
			foreach (var macro in PatchFile.Array(element, "macros"))
			{
				var made = track.Macro(MacroOf(macro.GetProperty("macro")));
				if (PatchFile.String(macro, "follow") is { } follow)
					made.Follow(Cue(follow) as Cue ?? throw Error($"a macro follows '{follow}', which isn't a level"),
						PatchFile.Float(macro, "from") ?? 0f, PatchFile.Float(macro, "to") ?? 1f);
				else
					made.Set(PatchFile.Float(macro, "set") ?? throw Error($"track '{trackName}': a macro follows a cue or is \"set\""));
			}
		}
		SongTrack Track(JsonElement element, string where) =>
			PatchFile.String(element, "track") is { } trackName && tracks.TryGetValue(trackName, out var track)
				? track
				: throw Error($"{where} is played by a track that isn't one ({element})");

		Part PartOf(JsonElement element, string where)
		{
			if (PatchFile.Object(element, "part") is { } code)
				return bindings.Part(PatchFile.String(code, "code") ?? "") ?? throw Error($"{where}: no part is code named {code}");
			if (PatchFile.String(element, "line") is { } lineStep)
			{
				var notes = PatchFile.String(element, "notes") ?? throw Error($"{where}: a line has \"notes\"");
				var line = Step(lineStep, name) switch
				{
					Ticks.Quarter => Line.Quarters(notes),
					Ticks.Eighth => Line.Eighths(notes),
					Ticks.Sixteenth => Line.Sixteenths(notes),
					_ => Line.Triplets(notes),
				};
				if (PatchFile.Bool(element, "overChords"))
					line = line.OverChords();
				if (element.TryGetProperty("octave", out var shift))
					line = line.Octave(shift.GetInt32());
				return line;
			}
			if (PatchFile.String(element, "beat") is { } beatStep)
			{
				var rows = PatchFile.Object(element, "rows") is { } r
					? r.EnumerateObject().Select(p => (new DrumPiece(p.Name), p.Value.GetString() ?? "")).ToArray()
					: throw Error($"{where}: a beat has \"rows\"");
				return Step(beatStep, name) switch
				{
					Ticks.Eighth => Beat.Eighths(rows),
					Ticks.Sixteenth => Beat.Sixteenths(rows),
					Ticks.Triplet => Beat.Triplets(rows),
					_ => throw Error($"{where}: a beat is in eighths, sixteenths or triplets"),
				};
			}
			if (PatchFile.String(element, "strum") is { } strumStep)
			{
				var notes = PatchFile.String(element, "notes") ?? throw Error($"{where}: a strum has \"notes\"");
				var strum = Step(strumStep, name) switch
				{
					Ticks.Quarter => Strum.Quarters(notes),
					Ticks.Eighth => Strum.Eighths(notes),
					Ticks.Sixteenth => Strum.Sixteenths(notes),
					_ => throw Error($"{where}: a strum is in quarters, eighths or sixteenths"),
				};
				if (PatchFile.String(element, "voicing") is { } voicing)
					strum = strum.Voicing(Enum.TryParse<Voicing>(voicing, ignoreCase: true, out var v) ? v : throw Error($"{where}: \"{voicing}\" isn't a voicing"));
				if (PatchFile.Float(element, "spread") is { } spread)
					strum = strum.Spread(spread);
				return strum;
			}
			throw Error($"{where}: a part is a \"line\", a \"beat\", a \"strum\" or code");
		}

		var sections = new List<Section>();
		var byName = new Dictionary<string, Section>(StringComparer.Ordinal);
		foreach (var element in PatchFile.Array(root, "sections"))
		{
			var sectionName = PatchFile.String(element, "name") ?? throw Error("a section has a \"name\"");
			var bars = element.TryGetProperty("bars", out var b) ? b.GetInt32() : throw Error($"section '{sectionName}' has \"bars\"");
			var section = song.Section(sectionName, bars, PatchFile.String(element, "chords") ?? "I");
			if (!byName.TryAdd(sectionName, section))
				throw Error($"two sections are named '{sectionName}'");
			sections.Add(section);
			foreach (var part in PatchFile.Array(element, "parts"))
			{
				var where = $"in '{sectionName}', a part";
				var track = Track(part, where);
				if (PatchFile.String(part, "code") is { } code)
				{
					_ = bindings.Placement(code, section, track) ?? throw Error($"{where}: no placement is code named '{code}'");
					continue;
				}
				var placement = section.Play(track, PartOf(part, where));
				if (PatchFile.String(part, "when") is { } when)
					placement.When(ConditionText.Parse(when, bindings.Cue));
				if (PatchFile.String(part, "level") is { } levelCue)
					placement.Level(Cue(levelCue) as Cue ?? throw Error($"{where}: its level follows '{levelCue}', which isn't a level"));
				if (PatchFile.Bool(part, "onPhrase"))
					placement.OnPhrase();
				if (PatchFile.Object(part, "endWith") is { } fill)
					placement.EndWith(PartOf(fill, where + "'s fill"));
				if (PatchFile.String(part, "name") is { } partName)
					placement.Named(partName);
			}
			foreach (var sweep in PatchFile.Array(element, "sweeps"))
				section.Sweep(Track(sweep, $"a sweep in '{sectionName}'"), MacroOf(sweep.GetProperty("macro")),
					PatchFile.Float(sweep, "from") ?? 0f, PatchFile.Float(sweep, "to") ?? 1f);
		}
		Section SectionNamed(string? sectionName) =>
			sectionName is not null && byName.TryGetValue(sectionName, out var section) ? section : throw Error($"no section is named '{sectionName}'");

		if (root.TryGetProperty("order", out var order))
			song.Order([.. order.EnumerateArray().Select(s => SectionNamed(s.GetString()))]);
		if (root.TryGetProperty("conduct", out var conduct))
		{
			var rules = conduct.EnumerateArray()
				.Select(rule => (When: PatchFile.String(rule, "when") is { } when ? ConditionText.Parse(when, bindings.Cue) : null,
					Then: SectionNamed(PatchFile.String(rule, "then"))))
				.ToArray();
			if (rules.Length == 0 || rules[^1].When is not null)
				throw Error("the last of \"conduct\"'s rules has a \"when\": the last has none, so a section is always chosen");
			song.Conduct(now =>
			{
				foreach (var (when, then) in rules)
					if (when is null || when.IsMet(now.Value))
						return then;
				return rules[^1].Then;
			});
		}
		foreach (var hook in PatchFile.Array(root, "hooks"))
		{
			var hookName = hook.GetString() ?? "";
			if (bindings.Hook is null)
				throw Error($"it has a hook, {hookName}, and nothing to run it");
			bindings.Hook(hookName, song, sections);
		}
		return song;
	}

	private static Meter? ParseMeter(string text)
	{
		var parts = text.Split('/');
		return parts.Length == 2 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var beats) && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var unit)
			? new Meter(beats, unit)
			: null;
	}

	private static Scale? ParseScale(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.String)
		{
			var text = element.GetString() ?? "";
			var space = text.IndexOf(' ', StringComparison.Ordinal);
			if (space < 0 || ParseNote(text[..space]) is not { } root)
				return null;
			var kind = Array.Find(Scales, s => s.Name == text[(space + 1)..]);
			return kind.Make?.Invoke(root);
		}
		return element.ValueKind == JsonValueKind.Object && ParseNote(PatchFile.String(element, "root") ?? "") is { } custom
			? Scale.Of(custom, PatchFile.String(element, "name") ?? "custom", [.. PatchFile.Array(element, "steps").Select(s => s.GetInt32())])
			: null;
	}

	private static Note? ParseNote(string text)
	{
		var at = Array.IndexOf(NoteNames, text);
		return at >= 0 ? (Note)at : null;
	}
}
