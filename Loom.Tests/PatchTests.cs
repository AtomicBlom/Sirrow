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
		// A set with no zones is refused as it is made, before a patch can take it
		Should.Throw<ArgumentException>(() => new SampleSet());
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

	[Test]
	public void A_tube_at_a_ratio_of_zero_is_refused() => ShouldRefuse(p =>
	{
		var breath = p.Level(Env.Gate);
		p.Out(p.Tube(ratio: 0f).DrivenBy(breath));
	});

	[Test]
	public void A_level_that_isnt_a_number_is_refused() => ShouldRefuse(p => p.Out(p.Sine(1f, Env.Gate, level: float.NaN)));

	[Test]
	public void A_sustain_above_one_is_refused() => ShouldRefuse(p => p.Out(p.Sine(1f, Env.Adsr(0.01f, 0.1f, 1.5f, 0.1f))));

	[Test]
	public void A_drive_past_one_is_refused() => ShouldRefuse(p =>
	{
		p.Out(p.Sine(1f, Env.Gate));
		p.Drive(2f);
	});

	[Test]
	public void A_filter_at_no_frequency_is_refused() => ShouldRefuse(p =>
	{
		p.Out(p.Sine(1f, Env.Gate));
		p.Filter(Filter.LowPass(0f));
	});

	[Test]
	public void A_pitch_drop_over_no_time_is_refused() => ShouldRefuse(p =>
	{
		p.Out(p.Sine(1f, Env.Decay(0.2f)));
		p.PitchDrop(12f, 0f);
	});

	[Test]
	public void A_mode_that_never_rings_down_is_refused() => ShouldRefuse(p =>
	{
		var knock = p.Sine(1f, Env.Decay(0.005f)).Fixed(2000f);
		p.Out(p.Modes(Mode.At(1f, 0f)).DrivenBy(knock));
	});
}
