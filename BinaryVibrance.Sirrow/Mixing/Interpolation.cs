using System.Runtime.CompilerServices;

namespace BinaryVibrance.Sirrow.Mixing;

/// <summary>Reading samples between samples: how a clip plays at another pitch and loads at another rate.</summary>
internal static class Interpolation
{
	/// <summary>
	/// The 4-point, third-order Hermite curve through <paramref name="x1"/> and <paramref name="x2"/>, at
	/// <paramref name="t"/> (0..1) between them: smooth enough that a pitched clip doesn't buzz, cheap enough to run per
	/// sample per voice.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Hermite(float x0, float x1, float x2, float x3, float t)
	{
		var c1 = 0.5f * (x2 - x0);
		var c2 = x0 - 2.5f * x1 + 2f * x2 - 0.5f * x3;
		var c3 = 0.5f * (x3 - x0) + 1.5f * (x1 - x2);
		return ((c3 * t + c2) * t + c1) * t + x1;
	}

	/// <summary>
	/// Channel <paramref name="channel"/> of frame <paramref name="frame"/> in interleaved <paramref name="samples"/>:
	/// silence past either end, or wrapped round when <paramref name="loop"/>.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float At(float[] samples, int channels, int channel, int frame, int frames, bool loop)
	{
		if ((uint)frame >= (uint)frames)
		{
			if (!loop)
				return 0f;
			frame %= frames;
			if (frame < 0)
				frame += frames;
		}
		return samples[frame * channels + channel];
	}
}
