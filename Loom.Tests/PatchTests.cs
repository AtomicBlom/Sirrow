using Loom.Audio.Synth.Chip;

namespace Loom.Tests;

/// <summary>Writing a patch: the builder's routing and the rules it checks.</summary>
public class PatchTests
{
	private static void ShouldRefuse(Action<PatchBuilder> build)
	{
		var thrown = Should.Throw<PatchException>(() => Patch.Build("broken", build));
		thrown.PatchName.ShouldBe("broken");
		thrown.Message.ShouldContain("broken");
	}

	[Test]
	public void More_than_16_operators_is_refused() => ShouldRefuse(p =>
	{
		Op? last = null;
		for (var i = 0; i < 17; i++)
			last = p.Sine(1f, Env.Gate);
		p.Out(last!);
	});

	[Test]
	public void A_patch_with_nothing_heard_is_refused() => ShouldRefuse(p => p.Sine(1f, Env.Gate));

	[Test]
	public void An_operator_fed_by_a_later_one_is_refused() => ShouldRefuse(p =>
	{
		var carrier = p.Sine(1f, Env.Gate);
		var modulator = p.Sine(2f, Env.Gate);
		carrier.ModulatedBy(modulator);
		p.Out(carrier);
	});

	[Test]
	public void An_operator_of_another_patch_is_refused()
	{
		Op? stray = null;
		Patch.Build("other", p => p.Out(stray = p.Sine(1f, Env.Gate)));
		ShouldRefuse(p => p.Out(p.Sine(1f, Env.Gate).ModulatedBy(stray!)));
		ShouldRefuse(p => p.Out(stray!));
	}

	[Test]
	public void Modulation_or_feedback_on_anything_but_a_sine_is_refused()
	{
		ShouldRefuse(p =>
		{
			var modulator = p.Sine(1f, Env.Gate);
			p.Out(p.Wave(Waveform.Saw, 1f, Env.Gate).ModulatedBy(modulator));
		});
		ShouldRefuse(p => p.Out(p.Noise(Env.Gate).Feedback(0.5f)));
	}

	[Test]
	public void Drivers_on_anything_but_a_string_or_tube_are_refused() => ShouldRefuse(p =>
	{
		var driver = p.Level(Env.Gate);
		p.Out(p.Sine(1f, Env.Gate).DrivenBy(driver));
	});

	[Test]
	public void Too_many_strings_or_tubes_are_refused()
	{
		ShouldRefuse(p => p.Out(p.String(), p.String(), p.String()));
		ShouldRefuse(p =>
		{
			var breath = p.Level(Env.Gate);
			p.Out(p.Tube().DrivenBy(breath), p.Tube().DrivenBy(breath));
		});
	}

	[Test]
	public void A_tube_with_no_driver_or_a_sample_with_no_samples_is_refused()
	{
		ShouldRefuse(p => p.Out(p.Tube()));
		ShouldRefuse(p => p.Out(p.Sample(new SampleSet())));
		ShouldRefuse(p => p.Out(p.Sample(null!)));
	}

	[Test]
	public void A_two_operator_patch_routes_as_written()
	{
		var patch = Patch.Build("fm", p =>
		{
			var modulator = p.Sine(2f, Env.Decay(0.3f));
			var carrier = p.Sine(1f, Env.Gate).ModulatedBy(modulator, 1.5f);
			p.Out(carrier);
		});

		patch.Name.ShouldBe("fm");
		patch.Operators.Length.ShouldBe(2);
		patch.Operators[0].Ratio.ShouldBe(2f);
		patch.Operators[1].Modulators.ShouldBe([new Modulation(0, 1.5f)]);
		patch.Outputs.ShouldBe([new OutputTap(1, 1f)]);
	}

	[Test]
	public void Out_taps_at_one_or_at_a_level()
	{
		var patch = Patch.Build("taps", p =>
		{
			var a = p.Sine(1f, Env.Gate);
			var b = p.Sine(2f, Env.Gate);
			p.Out(a, b.At(0.3f));
		});

		patch.Outputs.ShouldBe([new OutputTap(0, 1f), new OutputTap(1, 0.3f)]);
	}
}
