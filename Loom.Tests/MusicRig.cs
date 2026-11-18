using Loom.Audio.Synth;
using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Music;

namespace Loom.Tests;

/// <summary>A music player on an offline session's master bus at 48 kHz, stepped as a game would: Update, then a block.</summary>
internal sealed class MusicRig : IDisposable
{
	public const int Rate = 48_000;
	public const int Beat = 24_000;    // at 120 beats a minute
	public const int Bar = 96_000;     // a bar of 4/4 at 120

	/// <summary>A rig whose player renders <paramref name="aheadSeconds"/> ahead (live, by default).</summary>
	public MusicRig(double aheadSeconds = 0)
	{
		Session = new OfflineSession(Rate);
		Player = new MusicPlayer(Session.Engine, Session.Engine.Master, aheadSeconds);
	}

	public OfflineSession Session { get; }
	public MusicPlayer Player { get; }

	/// <summary>A short click at 1000 Hz: for onsets.</summary>
	public static Patch Click => Patch.Build("click", p => p.Out(p.Sine(1f, Env.Decay(0.01f)).Fixed(1000f)));

	/// <summary>Renders <paramref name="frames"/> in blocks of 256, the player's Update before each.</summary>
	public void Step(int frames)
	{
		for (var done = 0; done < frames;)
		{
			var n = Math.Min(256, frames - done);
			Player.Update();
			Session.Render(n);
			done += n;
		}
	}

	/// <summary>Steps up to frame <paramref name="frame"/> of what has been rendered since the session began or was cleared.</summary>
	public void StepTo(long frame) => Step((int)(frame - Session.Frames));

	public float[] Left() => Session.Left();

	/// <summary>
	/// Each onset: the first frame after at least 1000 frames of silence where a sample's magnitude passes 0.01.
	/// </summary>
	public static List<int> Onsets(float[] samples)
	{
		var onsets = new List<int>();
		var quiet = 1000;
		for (var i = 0; i < samples.Length; i++)
		{
			if (MathF.Abs(samples[i]) > 0.01f)
			{
				if (quiet >= 1000)
					onsets.Add(i);
				quiet = 0;
			}
			else
				quiet++;
		}
		return onsets;
	}

	public double Pitch(int from, int length) => AudioAnalysis.PeakFrequency(Left().AsSpan(from, length), Rate, 50, 4000);

	public float Rms(int from, int length) => AudioAnalysis.Rms(Left().AsSpan(from, length));

	public float Level(int from, int length, float hz) => AudioAnalysis.LevelAt(Left().AsSpan(from, length), hz, Rate);

	public void Dispose()
	{
		Player.Dispose();
		Session.Dispose();
	}
}
