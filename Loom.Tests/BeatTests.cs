using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Music;

namespace Loom.Tests;

/// <summary>The drum grid: hits, accents and ghosts where they fall; rows that don't agree refused.</summary>
public class BeatTests
{
	[Test]
	public void Hits_accents_and_ghosts()
	{
		var hits = ((Beat.Parsed)Beat.Eighths((Kit.Kick, "x... X..o")).Parse(Meter.FourFour)).Hits;

		hits.Select(h => h.Tick).ShouldBe([0, 1920, 3360]);
		hits.Select(h => h.Velocity).ShouldBe([0.75f, 1f, 0.35f]);
	}

	[Test]
	public void Rows_of_different_lengths_or_part_bars_are_refused()
	{
		Should.Throw<NotationException>(() => Beat.Eighths((Kit.Kick, "x... x..."), (Kit.Snare, "..x. ..x. ..x. ..x.")).Parse(Meter.FourFour));
		Should.Throw<NotationException>(() => Beat.Eighths((Kit.Kick, "x... x.")).Parse(Meter.FourFour));
	}
}
