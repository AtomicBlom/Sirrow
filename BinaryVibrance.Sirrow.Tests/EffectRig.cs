using BinaryVibrance.Sirrow;

namespace BinaryVibrance.Sirrow.Tests;

/// <summary>Runs an effect directly, in blocks as a bus would hand them, at 48 kHz: for the effects' tests.</summary>
internal static class EffectRig
{
	public const int Rate = 48_000;

	public static T Prepared<T>(T effect) where T : AudioEffect
	{
		effect.Prepare(Rate);
		return effect;
	}

	/// <summary>Processes both channels in place, a block of at most <see cref="AudioEffect.MaxBlockFrames"/> at a time.</summary>
	public static void Run(AudioEffect effect, float[] left, float[] right)
	{
		for (var offset = 0; offset < left.Length; offset += AudioEffect.MaxBlockFrames)
		{
			var n = Math.Min(AudioEffect.MaxBlockFrames, left.Length - offset);
			effect.Process(left.AsSpan(offset, n), right.AsSpan(offset, n));
		}
	}

	public static float[] Sine(double hz, int length, float amplitude = 1f)
	{
		var samples = new float[length];
		for (var n = 0; n < length; n++)
			samples[n] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * n / Rate));
		return samples;
	}

	public static float[] Impulse(int length)
	{
		var samples = new float[length];
		samples[0] = 1f;
		return samples;
	}

	public static float[] Noise(int length, float amplitude, int seed = 1)
	{
		var random = new Random(seed);
		var samples = new float[length];
		for (var n = 0; n < length; n++)
			samples[n] = amplitude * (2f * random.NextSingle() - 1f);
		return samples;
	}

	/// <summary>
	/// The bytes the effect allocates over 512 blocks of a sine once warmed up over 64, its parameters left alone: the
	/// least of up to five such windows, as <see cref="Allocations.ShouldAllocateNothing"/> takes them, since a one-off
	/// the runtime makes counts in one window only. Each effect's test of this is the audio thread's rule: nothing
	/// allocated once running.
	/// </summary>
	public static long AllocatedWhileRunning(AudioEffect effect)
	{
		var left = Sine(440, AudioEffect.MaxBlockFrames, 0.5f);
		var right = Sine(660, AudioEffect.MaxBlockFrames, 0.5f);
		var l = new float[left.Length];
		var r = new float[right.Length];

		void Block()
		{
			left.CopyTo(l, 0);
			right.CopyTo(r, 0);
			effect.Process(l, r);
		}

		for (var i = 0; i < 64; i++)
			Block();
		var least = long.MaxValue;
		for (var attempt = 0; attempt < 5 && least > 0; attempt++)
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			for (var i = 0; i < 512; i++)
				Block();
			least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
		}
		return least;
	}
}
