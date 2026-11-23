namespace BinaryVibrance.Sirrow.Synth.Effects;

/// <summary>
/// Effects as makers, for a chip group's or a song group's list, each setting its parameters on a new instance:
/// <c>song.Group("guitars", Fx.Distortion(24, cabinet: Cabinet.FourByTwelve), Fx.Reverb(1.2f, wet: 0.2f, dry: 1))</c>.
/// </summary>
public static class Fx
{
	/// <summary>
	/// A <see cref="Effects.Distortion"/>, oversampled <paramref name="oversampling"/> times (1, 2 or 4): 4 is the cleanest
	/// and costs the most, about five times 1's.
	/// </summary>
	public static Func<AudioEffect> Distortion(float driveDb = 12f, float toneHz = 4000f, DistortionCurve curve = DistortionCurve.Soft,
		Cabinet cabinet = Cabinet.None, float outputDb = -6f, int oversampling = 4) => () =>
	{
		var effect = new Distortion(curve, cabinet, oversampling);
		effect.DriveDb.Value = driveDb;
		effect.ToneHz.Value = toneHz;
		effect.OutputDb.Value = outputDb;
		return effect;
	};

	/// <summary>A <see cref="Effects.Reverb"/>; its defaults suit a bus fed by sends, all wet.</summary>
	public static Func<AudioEffect> Reverb(float decaySeconds = 1.8f, float wet = 1f, float dry = 0f) => () =>
	{
		var effect = new Reverb();
		effect.DecaySeconds.Value = decaySeconds;
		effect.Wet.Value = wet;
		effect.Dry.Value = dry;
		return effect;
	};

	/// <summary>A <see cref="Effects.Delay"/> of <paramref name="seconds"/> (<see cref="Effects.Delay.Beats"/> gives it in time with a song).</summary>
	public static Func<AudioEffect> Delay(float seconds, float feedback = 0.35f, bool pingPong = false, float wet = 1f, float dry = 0f) => () =>
	{
		var effect = new Delay(pingPong, MathF.Max(2f, seconds + 0.1f));
		effect.TimeSeconds.Value = seconds;
		effect.Feedback.Value = feedback;
		effect.Wet.Value = wet;
		effect.Dry.Value = dry;
		return effect;
	};

	/// <summary>A <see cref="Effects.Chorus"/>.</summary>
	public static Func<AudioEffect> Chorus(float mix = 0.5f) => () =>
	{
		var effect = new Chorus();
		effect.Mix.Value = mix;
		return effect;
	};

	/// <summary>A <see cref="Effects.Compressor"/>: on a group of drums, glue and punch.</summary>
	public static Func<AudioEffect> Compressor(float thresholdDb = -18f, float ratio = 4f, float attackSeconds = 0.01f,
		float releaseSeconds = 0.15f, float makeupDb = 0f, float kneeDb = 6f, bool keyed = false) => () =>
	{
		var effect = new Compressor(keyed);
		effect.ThresholdDb.Value = thresholdDb;
		effect.Ratio.Value = ratio;
		effect.AttackSeconds.Value = attackSeconds;
		effect.ReleaseSeconds.Value = releaseSeconds;
		effect.MakeupDb.Value = makeupDb;
		effect.KneeDb.Value = kneeDb;
		return effect;
	};

	/// <summary>
	/// A keyed <see cref="Effects.Compressor"/> that ducks: on the bass's group, it turns the bass down while what sends to
	/// the chip's key (<see cref="Chip.ChipSend.Key"/>, a kick) sounds, and lets it back up over
	/// <paramref name="releaseSeconds"/>. The key is heard after its voices' gain, so the threshold is measured against
	/// the key as loud as it plays.
	/// </summary>
	public static Func<AudioEffect> Duck(float thresholdDb = -30f, float ratio = 8f, float attackSeconds = 0.002f, float releaseSeconds = 0.12f) =>
		Compressor(thresholdDb, ratio, attackSeconds, releaseSeconds, kneeDb: 3f, keyed: true);

	/// <summary>An <see cref="Effects.Eq"/> with its shelves set.</summary>
	public static Func<AudioEffect> Eq(float lowDb = 0f, float highDb = 0f) => () =>
	{
		var effect = new Eq();
		effect.LowDb.Value = lowDb;
		effect.HighDb.Value = highDb;
		return effect;
	};
}
