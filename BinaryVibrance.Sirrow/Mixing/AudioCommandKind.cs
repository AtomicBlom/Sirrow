namespace BinaryVibrance.Sirrow.Mixing;

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
	/// <summary>The listener is at (A, B, C).</summary>
	ListenerPosition,
	/// <summary>The listener faces (A, B, C), a unit vector.</summary>
	ListenerForward,
	/// <summary>The listener's up is (A, B, C), a unit vector.</summary>
	ListenerUp,
	/// <summary>The listener moves at (A, B, C) units a second.</summary>
	ListenerVelocity,
	/// <summary>
	/// Voice <see cref="AudioCommand.Index"/>'s 3D settings: minimum distance A, maximum B, rolloff factor C, Doppler factor D,
	/// rolloff model <see cref="AudioCommand.Frames"/>. Sent before the voice's play, which keeps it when its
	/// <see cref="AudioCommand.Spatial"/> is set.
	/// </summary>
	VoiceSpatial,
	/// <summary>Voice <see cref="AudioCommand.Index"/> is at (A, B, C). Sent before a 3D play, and after as it moves.</summary>
	VoicePosition,
	/// <summary>Voice <see cref="AudioCommand.Index"/> moves at (A, B, C) units a second.</summary>
	VoiceVelocity,
	/// <summary>Bus <see cref="AudioCommand.Index"/> runs the <see cref="AudioEffect"/> in <see cref="AudioCommand.Payload"/> after the ones it has.</summary>
	AddEffect,
	/// <summary>
	/// Send <see cref="AudioCommand.Index"/> now exists: bus <see cref="AudioCommand.Bus"/> mixed into bus
	/// <see cref="AudioCommand.Frames"/> at level A.
	/// </summary>
	AddSend,
	/// <summary>Move send <see cref="AudioCommand.Index"/>'s level to A over <see cref="AudioCommand.Frames"/>.</summary>
	SendLevel,
	/// <summary>The buses below the master, in the order to finish them: the <c>int[]</c> in <see cref="AudioCommand.Payload"/>.</summary>
	MixOrder,
}
