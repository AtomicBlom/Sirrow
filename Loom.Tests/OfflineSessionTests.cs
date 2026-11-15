using Loom.Audio;
using Loom.Audio.Synth;

namespace Loom.Tests;

/// <summary>An engine rendered on request: what it keeps, a source finishing mid-block, and renders that repeat exactly.</summary>
public class OfflineSessionTests
{
	// Writes `level` to both channels for `frames` frames, then finishes
	private sealed class Burst(float level, int frames) : AudioSource
	{
		private int _done;

		protected override void OnStart(int sampleRate) => _done = 0;

		protected override bool Render(Span<float> left, Span<float> right, in AudioRenderInfo info)
		{
			var n = Math.Min(info.Frames, frames - _done);
			left[..n].Fill(level);
			right[..n].Fill(level);
			_done += n;
			return _done < frames;
		}
	}

	[Test]
	public void Rendering_keeps_the_frames_and_clearing_forgets_them()
	{
		using var session = new OfflineSession();
		session.Render(1000);

		session.Frames.ShouldBe(1000);
		session.Samples.Length.ShouldBe(2000);

		session.Clear();
		session.Frames.ShouldBe(0);
		session.Samples.Length.ShouldBe(0);
	}

	[Test]
	public void A_source_that_finishes_mid_block_is_silent_after_and_freed()
	{
		using var session = new OfflineSession();
		var burst = new Burst(0.25f, 300);
		session.Engine.Play(burst);

		session.Render(1024);

		var left = session.Left();
		left[299].ShouldBe(0.25f, 1e-6f);
		left[300].ShouldBe(0f);
		burst.IsPlaying.ShouldBeFalse();
	}

	[Test]
	public void Two_sessions_set_up_alike_render_the_same_bits()
	{
		float[] RenderOnce()
		{
			using var session = new OfflineSession();
			session.Engine.Play(new Burst(0.4f, 700), pan: 0.3f);
			session.Render(2000);
			return session.Samples.ToArray();
		}

		RenderOnce().ShouldBe(RenderOnce());
	}
}
