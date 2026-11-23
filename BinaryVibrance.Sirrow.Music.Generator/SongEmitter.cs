using System.Text;

namespace BinaryVibrance.Sirrow.Music.Generator;

/// <summary>
/// A <c>sirrow-song/2</c> file as the class that builds it: <c>static partial class OpenRoad { public static Song Build()
/// … }</c>, the calls a person would have made, in the order the file has them. A piece that is code is a partial
/// member declared here and called where the piece was; a hook is a partial method called once the rest is built.
/// </summary>
internal sealed class SongEmitter(MusicContext context)
{
	private const string I = "        ";

	private static readonly string[] Notes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

	private static readonly string[] NoteMembers = ["C", "Cs", "D", "Ds", "E", "F", "Fs", "G", "Gs", "A", "As", "B"];

	private static readonly Dictionary<string, string> Scales = new()
	{
		["major"] = "Major",
		["minor"] = "Minor",
		["dorian"] = "Dorian",
		["phrygian"] = "Phrygian",
		["lydian"] = "Lydian",
		["mixolydian"] = "Mixolydian",
		["harmonic minor"] = "HarmonicMinor",
		["pentatonic major"] = "PentatonicMajor",
		["pentatonic minor"] = "PentatonicMinor",
		["blues"] = "Blues",
	};

	private readonly ConditionCode _conditions = new(context);

	/// <summary>The class for the song file <paramref name="root"/> (from <paramref name="fileName"/>), in its namespace.</summary>
	/// <exception cref="JsonError">The file says something that isn't a song.</exception>
	public string Class(Json root, string fileName, string space)
	{
		var name = root["name"]?.Str ?? throw new JsonError("a song file has a \"name\"", root.Span);
		var type = Code.Identifier(Code.Pascal(StemOf(fileName)));
		var vars = new HashSet<string> { "song", "now" };
		string Var(string wanted, string kind)
		{
			var made = Code.Camel(wanted);
			if (!vars.Add(made))
			{
				made += kind;
				for (var n = 2; !vars.Add(made); n++)
					made = Code.Camel(wanted) + kind + n;
			}
			return made;
		}

		var body = new List<string>();
		var init = new List<string>();
		if (root["tempo"]?.Num is { } tempo)
			init.Add($"Tempo = {Code.F(tempo)}");
		if (root["meter"]?.Str is { } meter)
		{
			var parts = meter.Split('/');
			if (parts.Length != 2 || !int.TryParse(parts[0], out _) || !int.TryParse(parts[1], out _))
				throw new JsonError($"\"{meter}\" isn't a meter (\"3/4\")", root["meter"]!.Span);
			init.Add($"Meter = new Meter({parts[0]}, {parts[1]})");
		}
		if (root["scale"] is { } scale)
			init.Add($"Scale = {ScaleCode(scale)}");
		if (root["voices"]?.Num is { } voices)
			init.Add($"Voices = {voices}");
		body.Add($"var song = new Song({Code.Quote(name)}){(init.Count > 0 ? " { " + string.Join(", ", init) + " }" : "")};");
		if (root["effects"] is JsonObj effects)
			foreach (var (key, value, span) in effects.Members)
			{
				var property = key switch
				{
					"space" => "SpaceEffects",
					"echo" => "EchoEffects",
					"master" => "MasterEffects",
					_ => throw new JsonError($"\"{key}\" isn't a song's effects (space, echo or master)", span),
				};
				body.Add($"song.{property} = [{string.Join(", ", value.Items.Select(context.Effect))}];");
			}

		var groups = new Dictionary<string, string>();
		foreach (var group in root["groups"]?.Items ?? [])
		{
			var groupName = group["name"]?.Str ?? throw new JsonError("a group has a \"name\"", group.Span);
			var v = Var(groupName, "Group");
			groups[groupName] = v;
			var fx = (group["effects"]?.Items ?? []).Select(context.Effect).ToArray();
			body.Add($"var {v} = song.Group({Code.Quote(groupName)}{string.Concat(fx.Select(f => ", " + f))});");
		}

		var tracks = new Dictionary<string, string>();
		var macroLines = new List<string>();
		foreach (var track in root["tracks"]?.Items ?? [])
		{
			var trackName = track["name"]?.Str ?? throw new JsonError("a track has a \"name\"", track.Span);
			if (tracks.ContainsKey(trackName))
				throw new JsonError($"two tracks are named '{trackName}'", track["name"]!.Span);
			var v = Var(trackName, "Track");
			tracks[trackName] = v;
			string plays;
			if (track["patch"] is { } patch)
			{
				var patchName = patch.Str ?? throw new JsonError("a track's \"patch\" is a name", patch.Span);
				if (!context.HasPatch(patchName))
					throw new JsonError($"track '{trackName}' plays patch '{patchName}', which isn't a patch file here or in {context.Options.Patches}", patch.Span);
				plays = context.Patch(patchName);
			}
			else if (track["kit"] is { } kit)
			{
				var kitName = kit.Str ?? throw new JsonError("a track's \"kit\" is a name", kit.Span);
				if (!context.HasKit(kitName))
					throw new JsonError($"track '{trackName}' plays kit '{kitName}', which {context.Options.Kits} doesn't have", kit.Span);
				plays = context.Kit(kitName);
			}
			else
				throw new JsonError($"track '{trackName}' plays a \"patch\" or a \"kit\"", track.Span);
			var group = "";
			if (track["group"] is { } g)
				group = ", " + (g.Str is { } groupName && groups.TryGetValue(groupName, out var gv) ? gv : throw new JsonError($"no group is named {g}", g.Span));
			var chain = new StringBuilder($"var {v} = song.Track({Code.Quote(trackName)}, {plays}{group})");
			if (track["humanise"] is JsonObj humanise)
				chain.Append($".Humanise({Code.F(humanise["ms"]?.Num ?? "0")}, {Code.F(humanise["velocity"]?.Num ?? "0")})");
			if (track["swing"] is JsonObj swing)
				chain.Append($".Swing({Code.F(swing["ratio"]?.Num ?? "0.5")}{(swing["step"]?.Str is { } step ? ", Ticks." + StepTicks(step, swing) : "")})");
			if (track["octave"]?.Num is { } octave)
				chain.Append($".Octave({octave})");
			if (track["level"]?.Num is { } level)
				chain.Append($".Level({Code.F(level)})");
			if (track["pan"]?.Num is { } pan)
				chain.Append($".Pan({Code.F(pan)})");
			body.Add(chain + ";");
			foreach (var macro in track["macros"]?.Items ?? [])
			{
				var made = $"{v}.Macro({context.Macro(macro["macro"] ?? throw new JsonError("a macro names its \"macro\"", macro.Span))})";
				if (macro["follow"]?.Str is { } follow)
					macroLines.Add($"{made}.Follow({context.Cue(follow)}, {Code.F(macro["from"]?.Num ?? "0")}, {Code.F(macro["to"]?.Num ?? "1")});");
				else
					macroLines.Add($"{made}.Set({Code.F(macro["set"]?.Num ?? throw new JsonError("a macro follows a cue or is \"set\"", macro.Span))});");
			}
		}
		body.AddRange(macroLines);

		var partials = new List<string>();
		var declared = new HashSet<string>();
		string Track(Json element)
		{
			var trackName = element["track"]?.Str;
			return trackName is not null && tracks.TryGetValue(trackName, out var v)
				? v
				: throw new JsonError($"a part is played by track '{trackName}', which isn't one", element["track"]?.Span ?? element.Span);
		}
		var sections = new Dictionary<string, string>();
		var order = new List<string>();
		foreach (var section in root["sections"]?.Items ?? [])
		{
			var sectionName = section["name"]?.Str ?? throw new JsonError("a section has a \"name\"", section.Span);
			if (sections.ContainsKey(sectionName))
				throw new JsonError($"two sections are named '{sectionName}'", section["name"]!.Span);
			var v = Var(sectionName, "Section");
			sections[sectionName] = v;
			order.Add(v);
			body.Add("");
			if (section["about"]?.Str is { } about)
				body.AddRange(Code.Comment(about, ""));
			var bars = section["bars"]?.Num ?? throw new JsonError($"section '{sectionName}' has \"bars\"", section.Span);
			body.Add($"var {v} = song.Section({Code.Quote(sectionName)}, {bars}{(section["chords"]?.Str is { } chords ? ", " + Code.Quote(chords) : "")});");
			foreach (var part in section["parts"]?.Items ?? [])
			{
				var track = Track(part);
				if (part["code"]?.Str is { } placementCode)
				{
					var member = Code.Identifier(placementCode);
					body.Add($"{member}({v}, {track});");
					if (declared.Add(member))
						partials.Add($"private static partial Placement {member}(Section section, SongTrack track);");
					continue;
				}
				var play = new StringBuilder($"{v}.Play({track}, {Part(part, partials, declared)})");
				if (part["when"] is { } when)
					play.Append($".When({_conditions.Placement(when)})");
				if (part["level"]?.Str is { } levelCue)
					play.Append($".Level({context.Cue(levelCue)})");
				if (part["onPhrase"]?.True == true)
					play.Append(".OnPhrase()");
				if (part["endWith"] is JsonObj fill)
					play.Append($".EndWith({Part(fill, partials, declared)})");
				if (part["name"]?.Str is { } placementName)
					play.Append($".Named({Code.Quote(placementName)})");
				body.Add(play + ";");
			}
			foreach (var sweep in section["sweeps"]?.Items ?? [])
				body.Add($"{v}.Sweep({Track(sweep)}, {context.Macro(sweep["macro"] ?? throw new JsonError("a sweep names its \"macro\"", sweep.Span))}, "
					+ $"{Code.F(sweep["from"]?.Num ?? "0")}, {Code.F(sweep["to"]?.Num ?? "1")});");
		}
		string SectionVar(Json named) =>
			named.Str is { } sectionName && sections.TryGetValue(sectionName, out var v) ? v : throw new JsonError($"no section is named {named}", named.Span);

		if (root["order"] is { } orderList)
			throw new JsonError("a song-wide \"order\" is loom-song/1's: give each section its \"exits\" (an order A, B, C is A's [{ \"then\": \"B\" }] and so on round)", orderList.Span);
		if (root["conduct"] is { } conduct)
			throw new JsonError("a song-wide \"conduct\" is loom-song/1's: its rules are each section's \"exits\"", conduct.Span);
		var exitLines = new List<string>();
		foreach (var section in root["sections"]?.Items ?? [])
		{
			var from = SectionVar(section["name"]!);
			var exits = section["exits"]?.Items ?? [];
			for (var i = 0; i < exits.Count; i++)
			{
				var to = SectionVar(exits[i]["then"] ?? throw new JsonError("an exit says \"then\"", exits[i].Span));
				if (exits[i]["when"] is { } when)
					exitLines.Add($"{from}.Exit({to}, {_conditions.Placement(when)});");
				else if (i < exits.Count - 1)
					throw new JsonError("an exit with no \"when\" is always taken, so it comes last", exits[i].Span);
				else
					exitLines.Add($"{from}.Then({to});");
			}
		}
		if (exitLines.Count > 0)
		{
			body.Add("");
			body.AddRange(exitLines);
		}
		foreach (var hook in root["hooks"]?.Items ?? [])
		{
			var member = Code.Identifier(hook.Str ?? throw new JsonError("a hook is a name (\"OnBuilt\")", hook.Span));
			body.Add($"{member}(song{string.Concat(order.Select(s => ", " + s))});");
			partials.Add($"static partial void {member}(Song song{string.Concat(order.Select(s => ", Section " + s))});");
		}
		body.Add("return song;");

		var text = new StringBuilder();
		text.AppendLine("// <auto-generated/>");
		text.AppendLine($"// Made from {fileName} by Sirrow's music generator: edit that file, or make a piece of it code (\"code\") in {type}.cs");
		text.AppendLine("#nullable enable");
		text.AppendLine("using BinaryVibrance.Sirrow.Synth.Chip;");
		text.AppendLine("using BinaryVibrance.Sirrow.Synth.Effects;");
		text.AppendLine("using BinaryVibrance.Sirrow.Synth.Music;");
		text.AppendLine();
		text.AppendLine($"namespace {space};");
		text.AppendLine();
		if (root["about"]?.Str is { } songAbout)
			foreach (var line in Code.Comment(songAbout, ""))
				text.AppendLine(line);
		text.AppendLine($"static partial class {type}");
		text.AppendLine("{");
		text.AppendLine("    public static Song Build()");
		text.AppendLine("    {");
		foreach (var line in body)
			foreach (var part in line.Split('\n'))
				text.AppendLine(part.Length == 0 ? "" : I + part);
		text.AppendLine("    }");
		foreach (var partial in partials)
		{
			text.AppendLine();
			text.AppendLine("    " + partial);
		}
		text.AppendLine("}");
		return text.ToString();
	}

	/// <summary>"open-road.song.json" without its ".song.json".</summary>
	public static string StemOf(string fileName)
	{
		var name = Path.GetFileName(fileName);
		var at = name.IndexOf('.');
		return at < 0 ? name : name.Substring(0, at);
	}

	private string Part(Json part, List<string> partials, HashSet<string> declared)
	{
		if (part["part"] is { } code)
		{
			var member = Code.Identifier(code["code"]?.Str ?? throw new JsonError("a part that is code names it (\"code\": \"Name\")", code.Span));
			if (declared.Add(member))
				partials.Add($"private static partial Part {member}();");
			return member + "()";
		}
		if (part["line"] is { } lineStep)
		{
			var line = $"Line.{Step(lineStep)}({Code.Quote(part["notes"]?.Str ?? throw new JsonError("a line has \"notes\"", part.Span))})";
			if (part["overChords"]?.True == true)
				line += ".OverChords()";
			if (part["octave"]?.Num is { } shift)
				line += $".Octave({shift})";
			return line;
		}
		if (part["beat"] is { } beatStep)
		{
			var rows = part["rows"] as JsonObj ?? throw new JsonError("a beat has \"rows\"", part.Span);
			var written = rows.Members.Select(r => $"({context.Piece(r.Key)}, {Code.Quote(r.Value.Str ?? "")})").ToArray();
			return written.Length == 1
				? $"Beat.{Step(beatStep)}({written[0]})"
				: $"Beat.{Step(beatStep)}(\n    {string.Join(",\n    ", written)})";
		}
		if (part["strum"] is { } strumStep)
		{
			var strum = $"Strum.{Step(strumStep)}({Code.Quote(part["notes"]?.Str ?? throw new JsonError("a strum has \"notes\"", part.Span))})";
			if (part["voicing"]?.Str is { } voicing)
				strum += $".Voicing(Voicing.{Code.Pascal(voicing)})";
			if (part["spread"]?.Num is { } spread)
				strum += $".Spread({Code.F(spread)})";
			return strum;
		}
		throw new JsonError("a part is a \"line\", a \"beat\", a \"strum\" or code (\"part\": { \"code\": \"Name\" })", part.Span);
	}

	private static string Step(Json step) => step.Str switch
	{
		"quarters" => "Quarters",
		"eighths" => "Eighths",
		"sixteenths" => "Sixteenths",
		"triplets" => "Triplets",
		_ => throw new JsonError($"{step} isn't a step (quarters, eighths, sixteenths or triplets)", step.Span),
	};

	private static string StepTicks(string step, Json where) => step switch
	{
		"quarters" => "Quarter",
		"eighths" => "Eighth",
		"sixteenths" => "Sixteenth",
		"triplets" => "Triplet",
		_ => throw new JsonError($"\"{step}\" isn't a step (quarters, eighths, sixteenths or triplets)", where.Span),
	};

	private static string ScaleCode(Json scale)
	{
		if (scale.Str is { } text)
		{
			var space = text.IndexOf(' ');
			var root = space > 0 ? Array.IndexOf(Notes, text.Substring(0, space)) : -1;
			if (root < 0 || !Scales.TryGetValue(text.Substring(space + 1), out var kind))
				throw new JsonError($"\"{text}\" isn't a scale (\"G major\", \"F# minor\")", scale.Span);
			return $"Scale.{kind}(Note.{NoteMembers[root]})";
		}
		var custom = Array.IndexOf(Notes, scale["root"]?.Str ?? "");
		if (custom < 0)
			throw new JsonError("a scale of its own has a \"root\" (\"G\")", scale.Span);
		var steps = (scale["steps"]?.Items ?? []).Select(s => s.Num ?? "0");
		return $"Scale.Of(Note.{NoteMembers[custom]}, {Code.Quote(scale["name"]?.Str ?? "custom")}{string.Concat(steps.Select(s => ", " + s))})";
	}
}
