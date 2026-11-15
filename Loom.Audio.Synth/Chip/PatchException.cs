namespace Loom.Audio.Synth.Chip;

/// <summary>A patch that can't be played, thrown from <see cref="Patch.Build"/>: which patch, and what is wrong with it.</summary>
public sealed class PatchException(string patchName, string message) : Exception($"Patch '{patchName}': {message}")
{
	/// <summary>The patch's name.</summary>
	public string PatchName { get; } = patchName;
}
