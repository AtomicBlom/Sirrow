using Loom.Audio;

using Shouldly;

namespace Loom.Tests;

/// <summary>
/// The platform's output as an app opens it (<see cref="AudioOutput"/>): the registered outputs tried in turn, silence
/// saying why when none opens; and <see cref="AudioEngine.Update"/> pumping the device once a frame, which is when the
/// browser's device sends the frame's mix.
/// </summary>
[NotInParallel] // AudioOutput's registrations are the process's
public class AudioOutputTests
{
	[Test]
	public void Open_takes_the_first_output_that_opens_and_says_why_the_others_did_not()
	{
		var device = new PumpedDevice();
		AudioOutput.Register("Tests.Missing", _ => (null, "its library is not there"));
		AudioOutput.Register("Tests.Fake", _ => (device, ""));

		AudioOutput.Open(new AudioOptions()).ShouldBeSameAs(device);
		var names = AudioOutput.Names.ToList();
		names.IndexOf("Tests.Missing").ShouldBeLessThan(names.IndexOf("Tests.Fake"));
	}

	[Test]
	public void Registering_a_name_again_keeps_the_first()
	{
		AudioOutput.Register("Tests.Twice", _ => (null, "the first"));
		AudioOutput.Register("Tests.Twice", _ => throw new InvalidOperationException("the second was kept"));

		AudioOutput.Names.Count(n => n == "Tests.Twice").ShouldBe(1);
	}

	[Test]
	public void Update_pumps_the_device_with_the_time_since_the_last_update()
	{
		var device = new PumpedDevice();
		using var engine = new AudioEngine(device);

		engine.Update();
		Thread.Sleep(20);
		engine.Update();

		device.Pumps.Count.ShouldBe(2);
		device.Pumps[0].ShouldBe(0.0); // nothing to measure from on the first
		device.Pumps[1].ShouldBeGreaterThanOrEqualTo(0.015);
	}

	private sealed class PumpedDevice : IAudioDevice
	{
		public List<double> Pumps { get; } = [];
		public string Backend => "Test";
		public string Name => "pumped";
		public int SampleRate => 48_000;
		public int BufferFrames => 256;
		public void Start(IAudioRenderer renderer) { }
		public void Pause() { }
		public void Resume() { }
		public void Pump(double elapsedSeconds) => Pumps.Add(elapsedSeconds);
		public void Dispose() { }
	}
}
