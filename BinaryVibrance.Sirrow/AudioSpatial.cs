namespace BinaryVibrance.Sirrow;

/// <summary>
/// How a 3D voice (<see cref="AudioEngine.PlayAt(AudioClip, System.Numerics.Vector3, AudioBus, float, float, bool, AudioSpatial?)"/>)
/// falls off with distance and shifts with motion. Distances are in the game's own units, the same as its positions.
/// </summary>
public readonly record struct AudioSpatial
{
	/// <summary>The defaults: full volume within 1 unit, inverse rolloff out to 100, real Doppler.</summary>
	public AudioSpatial()
	{
	}

	/// <summary>The distance within which the voice plays at full volume. More than zero. Default 1.</summary>
	public float MinDistance { get; init; } = 1f;

	/// <summary>The distance beyond which it gets no quieter. At least <see cref="MinDistance"/>. Default 100.</summary>
	public float MaxDistance { get; init; } = 100f;

	/// <summary>The curve between the two. Default <see cref="AudioRolloff.Inverse"/>.</summary>
	public AudioRolloff Rolloff { get; init; } = AudioRolloff.Inverse;

	/// <summary>How steep the curve is: 0 doesn't fall off at all, 1 is the model as written, 2 twice as steep. Default 1.</summary>
	public float RolloffFactor { get; init; } = 1f;

	/// <summary>
	/// How much the voice's and the listener's velocities shift its pitch: 0 never, 1 as sound really does, more to
	/// exaggerate it. A source's voice ignores it, as it does pitch. Default 1.
	/// </summary>
	public float DopplerFactor { get; init; } = 1f;
}
