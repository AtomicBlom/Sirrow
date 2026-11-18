using System.Text.Json.Serialization;

namespace Loom.Audio.Synth.Chip;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(KitManifest))]
internal sealed partial class KitManifestJson : JsonSerializerContext;
