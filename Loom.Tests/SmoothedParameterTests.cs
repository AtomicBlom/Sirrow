using Loom.Audio;

namespace Loom.Tests;

/// <summary>A smoothed parameter eases toward the game's value with its time constant, whether stepped a sample or a block at a time.</summary>
public class SmoothedParameterTests
{
	private static SmoothedParameter Rising(float seconds = 0.1f)
	{
		var parameter = new SmoothedParameter(0f, seconds);
		parameter.Prepare(48_000);
		parameter.Value = 1f;
		parameter.Begin();
		return parameter;
	}

	[Test]
	public void After_one_time_constant_it_has_covered_63_percent()
	{
		var parameter = Rising();
		for (var i = 0; i < 4800; i++)
			parameter.Next();
		parameter.Current.ShouldBe(0.6321f, 0.001f);

		for (var i = 4800; i < 48_000; i++)
			parameter.Next();
		parameter.Current.ShouldBeGreaterThan(0.9999f);
	}

	[Test]
	public void Read_a_block_at_a_time_it_still_arrives()
	{
		var parameter = Rising();
		for (var block = 0; block < 48_000 / 240; block++)
		{
			parameter.Begin();
			for (var i = 0; i < 240; i++)
				parameter.Next();
		}

		parameter.Current.ShouldBeGreaterThan(0.9999f);
	}

	[Test]
	public void Skipping_lands_where_stepping_does()
	{
		var stepped = Rising();
		for (var i = 0; i < 4800; i++)
			stepped.Next();

		Rising().Skip(4800).ShouldBe(stepped.Current, 1e-4f);
	}

	[Test]
	public void Rising_it_never_falls_back()
	{
		var parameter = Rising();
		var last = parameter.Current;
		for (var i = 0; i < 10_000; i++)
		{
			var next = parameter.Next();
			next.ShouldBeGreaterThanOrEqualTo(last);
			last = next;
		}
	}

	[Test]
	public void Snap_jumps_to_the_target()
	{
		var parameter = Rising();
		parameter.Next();

		parameter.Snap().ShouldBe(1f);
		parameter.Current.ShouldBe(1f);
	}

	[Test]
	public void Unprepared_it_steps_straight_there()
	{
		var parameter = new SmoothedParameter(0f, 0.1f) { Value = 0.5f };
		parameter.Begin();

		parameter.Next().ShouldBe(0.5f);
	}
}
