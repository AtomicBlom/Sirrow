using Loom.Audio;
using Loom.Audio.Synth;
using Loom.Audio.Synth.Chip;

namespace Loom.Tests;

/// <summary>A chip playing on the master bus of an offline session at 48 kHz, for the chip's tests.</summary>
internal sealed class ChipRig : IDisposable
{
	public const int Rate = 48_000;

	public ChipRig(ChipOptions? options = null, int rate = Rate)
	{
		Session = new OfflineSession(rate);
		Chip = new SynthChip(options);
		Session.Engine.Play(Chip);
	}

	public OfflineSession Session { get; }
	public SynthChip Chip { get; }

	/// <summary>A sine at the note's pitch, gated, heard at 1.</summary>
	public static Patch Tone => Patch.Build("tone", p => p.Out(p.Sine(1f, Env.Gate)));

	public void Seconds(double seconds) => Session.RenderSeconds(seconds);

	/// <summary>The left channel from <paramref name="from"/> seconds into what was rendered since the last clear, for <paramref name="length"/> seconds.</summary>
	public float[] Left(double from, double length) => Session.Left().AsSpan((int)(from * Rate), (int)(length * Rate)).ToArray();

	public float[] Right(double from, double length) => Session.Right().AsSpan((int)(from * Rate), (int)(length * Rate)).ToArray();

	public float RmsLeft(double from, double length) => AudioAnalysis.Rms(Left(from, length));

	public float PeakBoth(double from, double length) => MathF.Max(AudioAnalysis.Peak(Left(from, length)), AudioAnalysis.Peak(Right(from, length)));

	public double Pitch(double from, double length, double minHz = 50, double maxHz = 8000) =>
		AudioAnalysis.PeakFrequency(Left(from, length), Rate, minHz, maxHz);

	public void Dispose() => Session.Dispose();

	/// <summary>A sine of <paramref name="hz"/> at 0.5, mono, as a recording at <paramref name="rate"/>.</summary>
	public static SampleData SineSample(double hz, double seconds = 1.0, int rate = Rate)
	{
		var samples = new float[(int)(seconds * rate)];
		for (var i = 0; i < samples.Length; i++)
			samples[i] = (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / rate));
		return new SampleData(samples, rate);
	}
}
