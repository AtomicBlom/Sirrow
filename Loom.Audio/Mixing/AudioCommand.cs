namespace Loom.Audio.Mixing;

/// <summary>What a command from the game thread asks the mixer to do.</summary>
internal enum AudioCommandKind : byte
{
	/// <summary>Bus <see cref="AudioCommand.Index"/> now exists, mixed into bus <see cref="AudioCommand.Bus"/>.</summary>
	AddBus,
	/// <summary>Move bus <see cref="AudioCommand.Index"/>'s volume to <see cref="AudioCommand.A"/> over <see cref="AudioCommand.Frames"/>.</summary>
	BusVolume,
	/// <summary>Start the clip in <see cref="AudioCommand.Payload"/> on voice <see cref="AudioCommand.Index"/>: volume A, pitch B, pan C.</summary>
	PlayClip,
	/// <summary>Start the source in <see cref="AudioCommand.Payload"/> on voice <see cref="AudioCommand.Index"/>: volume A, pan C.</summary>
	PlaySource,
	/// <summary>Move voice <see cref="AudioCommand.Index"/>'s volume to A over <see cref="AudioCommand.Frames"/>.</summary>
	VoiceVolume,
	/// <summary>Set voice <see cref="AudioCommand.Index"/>'s pitch to A.</summary>
	VoicePitch,
	/// <summary>Move voice <see cref="AudioCommand.Index"/>'s pan to A across the next block.</summary>
	VoicePan,
	/// <summary>Fade voice <see cref="AudioCommand.Index"/> out over <see cref="AudioCommand.Frames"/>, then end it.</summary>
	StopVoice,
}

/// <summary>One command from the game thread to the mixer, copied through an <see cref="AudioEventQueue{T}"/>.</summary>
internal struct AudioCommand
{
	public AudioCommandKind Kind;
	public bool Loop;
	public int Index;
	public int Bus;
	public int Frames;
	public float A;
	public float B;
	public float C;
	public object? Payload;
}
