namespace BinaryVibrance.Sirrow.Mixing;

/// <summary>
/// A duck as the mixer runs it, worked out on the game thread: rates per frame and the threshold as a linear level, so
/// the audio thread only compares and adds.
/// </summary>
/// <param name="Trigger">The bus whose level ducks the target.</param>
/// <param name="Target">The bus turned down.</param>
/// <param name="DepthDb">How far the target goes down while the trigger is heard, in decibels: below zero.</param>
/// <param name="Threshold">The trigger's block peak, linear, above which it counts as heard.</param>
/// <param name="AttackDbPerFrame">How fast the target goes down, in decibels a frame; infinite for at once.</param>
/// <param name="ReleaseDbPerFrame">How fast it comes back up, in decibels a frame; infinite for at once.</param>
/// <param name="HoldFrames">How long the target stays down after the trigger falls quiet, so gaps between words don't pump.</param>
internal sealed record DuckSettings(
	int Trigger,
	int Target,
	float DepthDb,
	float Threshold,
	float AttackDbPerFrame,
	float ReleaseDbPerFrame,
	int HoldFrames);
