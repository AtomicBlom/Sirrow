using System.Numerics;

namespace Loom.Audio.Mixing;

/// <summary>
/// Where a 3D voice sits for the listener: how loud (distance), where between the speakers (direction), and how far its
/// pitch shifts (Doppler). Stereo panning only, as OpenAL's default renderer does: a voice behind the listener sounds
/// where its mirror in front would, since two speakers can't tell front from back.
/// </summary>
internal static class Spatializer
{
	private const float MinPitch = 0.25f;
	private const float MaxPitch = 4f;

	/// <summary>The voice's gain, its pan (-1 left to 1 right), and its pitch factor.</summary>
	public static (float Gain, float Pan, float Doppler) Compute(
		Vector3 listener, Vector3 right, Vector3 listenerVelocity,
		Vector3 position, Vector3 velocity, in AudioSpatial settings, float speedOfSound)
	{
		var offset = position - listener;
		var distance = offset.Length();
		var gain = Attenuation(distance, settings);
		if (distance < 1e-6f)
			return (gain, 0f, 1f); // at the listener: centred, and no direction to move along

		var toVoice = offset / distance;
		var pan = Math.Clamp(Vector3.Dot(toVoice, right), -1f, 1f);

		var doppler = 1f;
		var factor = settings.DopplerFactor;
		if (factor > 0f)
		{
			// f' = f (c + listener's speed toward the voice) / (c + voice's speed away from the listener)
			var towardVoice = factor * Vector3.Dot(listenerVelocity, toVoice);
			var awayFromListener = factor * Vector3.Dot(velocity, toVoice);
			var denominator = Math.Max(speedOfSound + awayFromListener, speedOfSound * 0.01f);
			doppler = Math.Clamp((speedOfSound + towardVoice) / denominator, MinPitch, MaxPitch);
		}
		return (gain, pan, doppler);
	}

	/// <summary>The gain at <paramref name="distance"/>, by the settings' rolloff model.</summary>
	public static float Attenuation(float distance, in AudioSpatial settings)
	{
		var min = settings.MinDistance;
		var max = settings.MaxDistance;
		var d = Math.Clamp(distance, min, max);
		var r = settings.RolloffFactor;
		var gain = settings.Rolloff switch
		{
			AudioRolloff.Inverse => min / (min + r * (d - min)),
			AudioRolloff.Linear => max > min ? 1f - r * (d - min) / (max - min) : 1f,
			AudioRolloff.Exponential => MathF.Pow(d / min, -r),
			_ => throw new InvalidOperationException($"Unknown rolloff {settings.Rolloff}."),
		};
		return Math.Clamp(gain, 0f, 1f);
	}
}
