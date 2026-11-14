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
	/// <summary>Duck <see cref="AudioCommand.Index"/> now exists, as the <see cref="DuckSettings"/> in <see cref="AudioCommand.Payload"/> say.</summary>
	AddDuck,
	/// <summary>Duck <see cref="AudioCommand.Index"/> works when A is 1, and lets its target back up when A is 0.</summary>
	DuckEnabled,
	/// <summary>Move the output's gain, after the master bus, to A over <see cref="AudioCommand.Frames"/>.</summary>
	OutputVolume,
}
