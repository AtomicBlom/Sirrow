namespace Loom.Audio.Sdl;

/// <summary><c>SDL_AudioSpec</c>: a sample format, a channel count and a rate, in that order.</summary>
internal struct SdlAudioSpec
{
	public uint Format;   // SDL_AudioFormat
	public int Channels;
	public int Freq;
}
