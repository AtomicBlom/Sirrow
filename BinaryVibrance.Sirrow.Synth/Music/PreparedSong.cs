namespace BinaryVibrance.Sirrow.Synth.Music;

/// <summary>
/// A song made ready to play (<see cref="MusicPlayer.Prepare"/>): compiled in its scale, its chip's voices and effects made.
/// That is the costly part of starting a song, and needs no engine, so a game can do it on a thread of its own and hand
/// the result to <see cref="MusicPlayer.Play(PreparedSong, float)"/> on the engine's. Played once.
/// </summary>
public sealed class PreparedSong
{
	internal PreparedSong(SongState state) => State = state;

	internal SongState State { get; }

	// Set by the player that plays it: a second play would share the first's chip
	internal bool Played { get; set; }

	/// <summary>The song it was made from.</summary>
	public Song Song => State.Song.Song;

	/// <summary>The scale it plays in.</summary>
	public Scale Scale => State.Song.Scale;
}
