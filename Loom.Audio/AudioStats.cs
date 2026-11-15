namespace Loom.Audio;

/// <summary>How the mixer is doing, for a debug display or a test: read from <see cref="AudioEngine.Stats"/>.</summary>
/// <param name="FramesRendered">Frames the mixer has rendered since it started: its clock.</param>
/// <param name="Callbacks">How many times the device has asked for samples.</param>
/// <param name="LastCallbackFrames">How many frames the device asked for last time: the buffer it is really running at.</param>
/// <param name="ActiveVoices">Voices playing now, as the game thread last heard.</param>
/// <param name="DroppedVoices">Plays dropped because every voice was busy.</param>
/// <param name="Peak">The loudest sample of the device's last buffer, before clipping: above 1 clips.</param>
/// <param name="RenderLoad">
/// How long the last render took, as a fraction of the time its samples last: 0.1 is 10% of the deadline. Near 1 the
/// output starts to drop out.
/// </param>
/// <param name="MaxRenderLoad">
/// The highest <paramref name="RenderLoad"/> since the mixer started (its first second left out, while the mix compiles),
/// or since <see cref="AudioEngine.ResetMaxRenderLoad"/>.
/// </param>
public readonly record struct AudioStats(
	long FramesRendered,
	long Callbacks,
	int LastCallbackFrames,
	int ActiveVoices,
	int DroppedVoices,
	float Peak,
	double RenderLoad,
	double MaxRenderLoad);
