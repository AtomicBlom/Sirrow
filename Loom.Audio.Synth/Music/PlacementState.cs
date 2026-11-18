namespace Loom.Audio.Synth.Music;

/// <summary>How a placement's playing is decided (<see cref="MusicPlayer.Force"/>): by its condition, or held on or off.</summary>
public enum PlacementState
{
	/// <summary>By its condition, as written.</summary>
	Auto,
	/// <summary>Playing, whatever its condition.</summary>
	On,
	/// <summary>Silent, whatever its condition.</summary>
	Off,
}
