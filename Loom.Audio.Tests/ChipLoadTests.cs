using System.Diagnostics;

using Loom.Audio.Synth.Chip;

namespace Loom.Tests;

/// <summary>What the chip costs: ten seconds rendered at its heaviest and at a usual load, against real time.</summary>
[NotInParallel]
public class ChipLoadTests
{
	private static double TimesRealTime(Patch patch, int voices)
	{
		using var rig = new ChipRig(new ChipOptions { Voices = voices });
		var instrument = rig.Chip.AddInstrument(patch);
		for (var i = 0; i < voices; i++)
			rig.Chip.NoteOn(instrument, 40 + i, 0.3f);
		rig.Seconds(2); // warm: every path compiled, and promoted by the tiered JIT
		rig.Session.Clear();
		var clock = Stopwatch.StartNew();
		rig.Seconds(10);
		clock.Stop();
		rig.Chip.Stats.ActiveVoices.ShouldBe(voices);
		return 10 / clock.Elapsed.TotalSeconds;
	}

	[Test]
	public void Thirty_two_voices_of_sixteen_operators_render_faster_than_real_time()
	{
		var heavy = Patch.Build("heavy", p =>
		{
			var taps = new OpTap[Patch.MaxOperators];
			for (var i = 0; i < taps.Length; i++)
				taps[i] = p.Sine(1f + i, Env.Gate, 1f / Patch.MaxOperators);
			p.Out(taps);
		});

		var speed = TimesRealTime(heavy, 32);
		Console.WriteLine($"32 voices x 16 operators: {speed:F1}x real time");
		// Measured 5.8 times on a Snapdragon X laptop
		// The figure is printed to compare a change by; a test only holds it to keeping up, since a shared or loaded machine
		// can't promise more
		speed.ShouldBeGreaterThan(1);
	}

	[Test]
	public void Sixteen_voices_of_four_operators_render_many_times_faster_than_real_time()
	{
		var usual = Patch.Build("usual", p =>
		{
			var m2 = p.Sine(3f, Env.Adsr(0.01f, 0.3f, 0.4f, 0.2f));
			var c2 = p.Sine(1f, Env.Adsr(0.01f, 0.3f, 0.7f, 0.2f)).ModulatedBy(m2);
			var m1 = p.Sine(1f, Env.Adsr(0.01f, 0.5f, 0.3f, 0.2f)).Feedback(0.3f);
			var c1 = p.Sine(2f, Env.Adsr(0.01f, 0.5f, 0.6f, 0.2f)).ModulatedBy(m1);
			p.Out(c1.At(0.5f), c2.At(0.5f));
			p.Filter(Filter.LowPass(4000f));
		});

		var speed = TimesRealTime(usual, 16);
		Console.WriteLine($"16 voices x 4 operators: {speed:F1}x real time");
		// Measured 17 to 21 times on a Snapdragon X laptop: the feedback and the filter are recursions, each sample waiting
		// on the one before, and cost half of it. Printed to compare a change by; held only to keeping up, as above
		speed.ShouldBeGreaterThan(1);
	}
}
