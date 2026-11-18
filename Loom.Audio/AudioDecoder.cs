using Loom.Audio.Decoding;
using Loom.Audio.Decoding.Flac;
using Loom.Audio.Decoding.Vorbis;

namespace Loom.Audio;

/// <summary>Decodes a sound file's bytes to samples at the file's own rate, on any thread, without an engine.</summary>
public static class AudioDecoder
{
	/// <summary>A WAV file's samples: PCM at 8 to 32 bits or float, mono or stereo, as <see cref="AudioEngine.LoadWav"/> reads.</summary>
	/// <exception cref="InvalidDataException">The bytes aren't a WAV file this reads.</exception>
	public static AudioData DecodeWav(ReadOnlySpan<byte> wav)
	{
		var (samples, channels, rate) = WavDecoder.Decode(wav);
		return new AudioData(samples, channels, rate);
	}

	/// <summary>An Ogg Vorbis file's samples, decoded whole: mono or stereo.</summary>
	/// <exception cref="InvalidDataException">The bytes aren't Ogg Vorbis, or are damaged.</exception>
	/// <exception cref="NotSupportedException">The file has more than two channels.</exception>
	public static AudioData DecodeOgg(ReadOnlySpan<byte> ogg) => ReadAll(new VorbisDecoder(new MemoryStream(ogg.ToArray(), writable: false)));

	/// <summary>A FLAC file's samples, decoded whole: mono or stereo.</summary>
	/// <exception cref="InvalidDataException">The bytes aren't FLAC, or are damaged.</exception>
	/// <exception cref="NotSupportedException">The file has more than two channels.</exception>
	public static AudioData DecodeFlac(ReadOnlySpan<byte> flac) => ReadAll(new FlacDecoder(new MemoryStream(flac.ToArray(), writable: false)));

	/// <summary>A WAV, Ogg Vorbis or FLAC file's samples, told apart by its first four bytes ("RIFF", "OggS" or "fLaC").</summary>
	/// <exception cref="InvalidDataException">The bytes are none of them, or are damaged.</exception>
	public static AudioData Decode(ReadOnlySpan<byte> file)
	{
		if (file.StartsWith("RIFF"u8))
			return DecodeWav(file);
		if (file.StartsWith("OggS"u8))
			return DecodeOgg(file);
		if (file.StartsWith("fLaC"u8))
			return DecodeFlac(file);
		throw new InvalidDataException("The bytes are neither a WAV file (RIFF), Ogg Vorbis (OggS) nor FLAC (fLaC).");
	}

	// Everything a stream decoder has, to its end
	private static AudioData ReadAll(IStreamDecoder decoder)
	{
		if (decoder.Channels > 2)
			throw new NotSupportedException($"The file has {decoder.Channels} channels: the engine plays mono and stereo.");
		var samples = new float[decoder.SampleRate * decoder.Channels];
		var count = 0;
		while (true)
		{
			if (count == samples.Length)
				Array.Resize(ref samples, samples.Length * 2);
			var frames = decoder.Read(samples.AsSpan(count));
			if (frames == 0)
				break;
			count += frames * decoder.Channels;
		}
		Array.Resize(ref samples, count);
		return new AudioData(samples, decoder.Channels, decoder.SampleRate);
	}
}
