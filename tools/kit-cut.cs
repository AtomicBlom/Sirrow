// Cuts a drum sample library down to a small kit for Loom.Audio.Synth's DrumKit.Load: Ogg files and a kit.json.
// Usage: dotnet run --file tools/kit-cut.cs -- <map.json> <outDir>
//
// map.json names each piece's layers and takes in the library (docs/synth.md, "Samples and kits"):
//   { "name": "rock", "source": "…", "pieces": [ { "piece": "kick", "gainDb": 0, "pan": 0, "choke": 0, "maxSeconds": 1.0,
//     "layers": [ { "upTo": 0.35, "takes": ["D:/libs/brd/kick_soft_1.flac", "…"] } ] } ] }
//
// For each take: converted to mono 44.1 kHz floats by ffmpeg; its start trimmed to 2 ms before the first sample above
// -50 dB of its peak; cut at maxSeconds, or sooner where the rest stays under -60 dB of its peak; its last 30 ms faded
// out. Then every take of a piece is scaled by the one factor that brings the piece's loudest peak to -1 dBFS, so the
// soft layers stay soft, and encoded to Ogg Vorbis (quality 5) as <piece>_<layer><take>.ogg: layer 1, 2, …, take a, b, ….
// It needs ffmpeg on the PATH, built with libvorbis.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const int Rate = 44_100;

if (args.Length < 2)
{
	Console.Error.WriteLine("Usage: dotnet run --file tools/kit-cut.cs -- <map.json> <outDir>");
	return 1;
}
var mapPath = Path.GetFullPath(args[0]);
var outDir = Path.GetFullPath(args[1]);

if (!HasVorbis(out var why))
{
	Console.Error.WriteLine(why);
	return 1;
}

JsonNode map;
try
{
	map = JsonNode.Parse(File.ReadAllText(mapPath)) ?? throw new InvalidDataException("it is empty");
}
catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
{
	Console.Error.WriteLine($"Can't read the map '{mapPath}': {ex.Message}");
	return 1;
}
var mapFolder = Path.GetDirectoryName(mapPath)!;
Directory.CreateDirectory(outDir);
var temp = Path.Combine(Path.GetTempPath(), "kit-cut-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(temp);

var manifestPieces = new JsonArray();
long totalBytes = 0;
var totalFiles = 0;
try
{
	foreach (var pieceNode in map["pieces"]?.AsArray() ?? [])
	{
		var piece = pieceNode!["piece"]!.GetValue<string>();
		var maxSeconds = pieceNode["maxSeconds"]?.GetValue<double>() ?? 1.0;
		var layers = pieceNode["layers"]!.AsArray();

		// Every take read and cut first, so the piece can be scaled as one
		var cut = new List<List<float[]>>();
		var loudest = 0f;
		foreach (var layerNode in layers)
		{
			var takes = new List<float[]>();
			foreach (var takeNode in layerNode!["takes"]!.AsArray())
			{
				var source = takeNode!.GetValue<string>();
				var sourcePath = Path.IsPathRooted(source) ? source : Path.Combine(mapFolder, source);
				var samples = Cut(Decode(sourcePath, temp), maxSeconds);
				takes.Add(samples);
				foreach (var s in samples)
					loudest = MathF.Max(loudest, MathF.Abs(s));
			}
			cut.Add(takes);
		}
		if (loudest <= 0f)
			throw new InvalidDataException($"piece '{piece}': every take is silent.");
		var scale = MathF.Pow(10f, -1f / 20f) / loudest;

		var manifestLayers = new JsonArray();
		long pieceBytes = 0;
		var pieceFiles = 0;
		for (var l = 0; l < cut.Count; l++)
		{
			var names = new JsonArray();
			for (var t = 0; t < cut[l].Count; t++)
			{
				var samples = cut[l][t];
				for (var i = 0; i < samples.Length; i++)
					samples[i] *= scale;
				var name = $"{piece}_{l + 1}{(char)('a' + t)}.ogg";
				var wav = Path.Combine(temp, "cut.wav");
				WriteWav(wav, samples);
				var ogg = Path.Combine(outDir, name);
				Run("ffmpeg", ["-y", "-hide_banner", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "5", ogg]);
				pieceBytes += new FileInfo(ogg).Length;
				pieceFiles++;
				names.Add((JsonNode)JsonValue.Create(name));
			}
			manifestLayers.Add((JsonNode)new JsonObject
			{
				["upTo"] = layers[l]!["upTo"]!.GetValue<double>(),
				["takes"] = names,
			});
		}
		manifestPieces.Add((JsonNode)new JsonObject
		{
			["piece"] = piece,
			["gainDb"] = pieceNode["gainDb"]?.GetValue<double>() ?? 0,
			["pan"] = pieceNode["pan"]?.GetValue<double>() ?? 0,
			["choke"] = pieceNode["choke"]?.GetValue<int>() ?? 0,
			["layers"] = manifestLayers,
		});
		Console.WriteLine($"{piece,-12} {pieceFiles,3} files {pieceBytes,10:N0} bytes");
		totalBytes += pieceBytes;
		totalFiles += pieceFiles;
	}
}
catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or NullReferenceException or FormatException)
{
	Console.Error.WriteLine($"kit-cut: {ex.Message}");
	return 1;
}
finally
{
	Directory.Delete(temp, recursive: true);
}

var manifest = new JsonObject
{
	["name"] = map["name"]?.GetValue<string>() ?? Path.GetFileName(outDir),
	["source"] = map["source"]?.GetValue<string>(),
	["pieces"] = manifestPieces,
};
File.WriteAllText(Path.Combine(outDir, "kit.json"), manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
Console.WriteLine($"{"total",-12} {totalFiles,3} files {totalBytes,10:N0} bytes, in {outDir}");
return 0;

// Whether ffmpeg is on the PATH and can encode Vorbis
static bool HasVorbis(out string why)
{
	try
	{
		var encoders = Run("ffmpeg", ["-hide_banner", "-encoders"]);
		why = "ffmpeg is on the PATH but has no libvorbis encoder: install a build with it (gyan.dev's full build has it).";
		return encoders.Contains("libvorbis", StringComparison.Ordinal);
	}
	catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
	{
		why = "ffmpeg isn't on the PATH: kit-cut needs it to read the library and write Ogg Vorbis (winget install ffmpeg).";
		return false;
	}
}

// A take as mono floats at 44.1 kHz, converted by ffmpeg from whatever it is
static float[] Decode(string source, string temp)
{
	if (!File.Exists(source))
		throw new FileNotFoundException($"no take '{source}'.", source);
	var wav = Path.Combine(temp, "take.wav");
	Run("ffmpeg", ["-y", "-hide_banner", "-loglevel", "error", "-i", source, "-ac", "1", "-ar", Rate.ToString(CultureInfo.InvariantCulture), "-c:a", "pcm_f32le", wav]);
	return ReadWav(wav);
}

// Trimmed to 2 ms before its first sample above -50 dB of its peak; cut at maxSeconds or where the rest stays under
// -60 dB of its peak; its last 30 ms faded out
static float[] Cut(float[] samples, double maxSeconds)
{
	var peak = 0f;
	foreach (var s in samples)
		peak = MathF.Max(peak, MathF.Abs(s));
	if (peak <= 0f)
		return samples;
	var onset = MathF.Pow(10f, -50f / 20f) * peak;
	var floor = MathF.Pow(10f, -60f / 20f) * peak;
	var first = Array.FindIndex(samples, s => MathF.Abs(s) > onset);
	var start = Math.Max(0, first - (int)(0.002 * Rate));
	var last = Array.FindLastIndex(samples, s => MathF.Abs(s) > floor);
	var end = Math.Min(Math.Min(samples.Length, start + (int)(maxSeconds * Rate)), last + 1);
	var cut = samples[start..Math.Max(start + 1, end)];
	var fade = Math.Min(cut.Length, (int)(0.03 * Rate));
	for (var i = 0; i < fade; i++)
		cut[cut.Length - fade + i] *= 1f - (i + 1f) / fade;
	return cut;
}

// A WAV file's 32-bit float samples, as ffmpeg writes them with pcm_f32le
static float[] ReadWav(string path)
{
	var bytes = File.ReadAllBytes(path);
	if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
		throw new InvalidDataException($"'{path}' isn't a WAV file.");
	var at = 12;
	while (at + 8 <= bytes.Length)
	{
		var id = Encoding.ASCII.GetString(bytes, at, 4);
		var size = BitConverter.ToInt32(bytes, at + 4);
		if (id == "data")
		{
			var count = Math.Min(size, bytes.Length - at - 8) / 4;
			var samples = new float[count];
			Buffer.BlockCopy(bytes, at + 8, samples, 0, count * 4);
			return samples;
		}
		at += 8 + size + (size & 1);
	}
	throw new InvalidDataException($"'{path}' has no data.");
}

// Mono 32-bit float samples at 44.1 kHz as a WAV file
static void WriteWav(string path, float[] samples)
{
	using var writer = new BinaryWriter(File.Create(path));
	var data = samples.Length * 4;
	writer.Write("RIFF"u8);
	writer.Write(36 + data);
	writer.Write("WAVEfmt "u8);
	writer.Write(16);
	writer.Write((short)3);       // IEEE float
	writer.Write((short)1);       // mono
	writer.Write(Rate);
	writer.Write(Rate * 4);
	writer.Write((short)4);
	writer.Write((short)32);
	writer.Write("data"u8);
	writer.Write(data);
	foreach (var s in samples)
		writer.Write(s);
}

// Runs a program to its end; its standard output, or an exception with its errors if it failed
static string Run(string program, string[] arguments)
{
	var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
	foreach (var argument in arguments)
		start.ArgumentList.Add(argument);
	using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} didn't start.");
	var output = process.StandardOutput.ReadToEndAsync();
	var errors = process.StandardError.ReadToEndAsync();
	process.WaitForExit();
	if (process.ExitCode != 0)
		throw new InvalidOperationException($"{program} failed ({process.ExitCode}): {errors.Result.Trim()}");
	return output.Result;
}
