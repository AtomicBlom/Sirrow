using System.Text.Json;

namespace Loom.Audio.Synth.Chip;

/// <summary>
/// A drum kit for a <see cref="SynthChip"/> (<see cref="SynthChip.AddKit"/>): pieces sampled in velocity layers and
/// alternate takes, or synthesised from patches, with choke groups. Load a sampled one from a manifest, or make a
/// synthesised one with <see cref="FromPatches"/>.
/// </summary>
public sealed class DrumKit
{
	private readonly DrumKitPiece[] _pieces;
	private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

	/// <summary>A kit named <paramref name="name"/> of <paramref name="pieces"/>; a piece named twice is the later one.</summary>
	public DrumKit(string name, params DrumKitPiece[] pieces)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(pieces);
		Name = name;
		_pieces = [.. pieces];
		for (var i = 0; i < _pieces.Length; i++)
		{
			var piece = _pieces[i];
			if (piece.Patch is null && piece.Layers.Length == 0)
				throw new ArgumentException($"Kit '{name}': piece '{piece.Piece.Id}' has neither layers nor a patch.", nameof(pieces));
			_index[piece.Piece.Id] = i;
		}
	}

	/// <summary>Its name.</summary>
	public string Name { get; }

	/// <summary>Its pieces.</summary>
	public IReadOnlyList<DrumKitPiece> Pieces => _pieces;

	/// <summary>The index of <paramref name="piece"/> in <see cref="Pieces"/>, or -1 if the kit hasn't it.</summary>
	public int IndexOf(DrumPiece piece) => piece.Id is not null && _index.TryGetValue(piece.Id, out var i) ? i : -1;

	/// <summary>
	/// A sampled kit from its manifest (kit.json, as <c>tools/kit-cut.cs</c> writes it), each file read through
	/// <paramref name="read"/> at its path relative to the manifest's folder and decoded (<see cref="AudioDecoder.Decode"/>).
	/// </summary>
	/// <param name="read">Gives a file's bytes by its path: from disk, an embedded resource, a package.</param>
	/// <param name="manifestPath">The manifest's path, as <paramref name="read"/> takes it.</param>
	/// <exception cref="InvalidDataException">The manifest can't be read, or names a missing file, an empty layer, or layers out of rising order.</exception>
	public static DrumKit Load(Func<string, byte[]> read, string manifestPath)
	{
		ArgumentNullException.ThrowIfNull(read);
		ArgumentNullException.ThrowIfNull(manifestPath);
		KitManifest manifest;
		try
		{
			manifest = JsonSerializer.Deserialize(read(manifestPath), KitManifestJson.Default.KitManifest)
				?? throw new InvalidDataException($"The kit manifest '{manifestPath}' is empty.");
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException)
		{
			throw new InvalidDataException($"The kit manifest '{manifestPath}' can't be read: {ex.Message}", ex);
		}

		var cut = Math.Max(manifestPath.LastIndexOf('/'), manifestPath.LastIndexOf('\\'));
		var folder = cut >= 0 ? manifestPath[..(cut + 1)] : "";
		var pieces = new List<DrumKitPiece>();
		foreach (var entry in manifest.Pieces)
		{
			void Fail(string message) => throw new InvalidDataException($"The kit manifest '{manifestPath}', piece '{entry.Piece}': {message}");

			if (string.IsNullOrWhiteSpace(entry.Piece))
				throw new InvalidDataException($"The kit manifest '{manifestPath}' has a piece with no name.");
			if (entry.Layers.Count == 0)
				Fail("it has no layers.");
			var layers = new DrumLayer[entry.Layers.Count];
			for (var l = 0; l < layers.Length; l++)
			{
				var layer = entry.Layers[l];
				if (layer.Takes.Count == 0)
					Fail($"layer {l + 1} has no takes.");
				if (l > 0 && layer.UpTo <= entry.Layers[l - 1].UpTo)
					Fail($"layer {l + 1} is up to {layer.UpTo}, not above the layer before it ({entry.Layers[l - 1].UpTo}): layers go softest first.");
				var takes = new SampleData[layer.Takes.Count];
				for (var t = 0; t < takes.Length; t++)
				{
					var path = folder + layer.Takes[t];
					byte[] bytes;
					try
					{
						bytes = read(path);
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException or ArgumentException)
					{
						throw new InvalidDataException($"The kit manifest '{manifestPath}', piece '{entry.Piece}': the file '{path}' can't be read: {ex.Message}", ex);
					}
					takes[t] = SampleData.From(AudioDecoder.Decode(bytes));
				}
				layers[l] = new DrumLayer(layer.UpTo, takes);
			}
			pieces.Add(new DrumKitPiece(new DrumPiece(entry.Piece), layers, entry.GainDb, entry.Pan, entry.Choke));
		}
		return new DrumKit(manifest.Name, [.. pieces]);
	}

	/// <summary>A synthesised kit: each piece a patch played at note 60, in a choke group (0 for none).</summary>
	public static DrumKit FromPatches(string name, params (DrumPiece Piece, Patch Patch, int ChokeGroup)[] pieces)
	{
		ArgumentNullException.ThrowIfNull(pieces);
		var made = new DrumKitPiece[pieces.Length];
		for (var i = 0; i < pieces.Length; i++)
			made[i] = new DrumKitPiece(pieces[i].Piece, [], ChokeGroup: pieces[i].ChokeGroup, Patch: pieces[i].Patch);
		return new DrumKit(name, made);
	}
}
