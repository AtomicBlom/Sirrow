using System.Diagnostics;

using Loom.Audio;
using Loom.Hosting.Desktop.Sdl;

namespace Loom.Tests;

/// <summary>
/// The desktop's audio device through SDL 3, on the machine's real output: it opens, asks for samples in real time on its
/// own thread, and stops cleanly. Renders silence, so a run makes no sound. SKIPPED where SDL3.dll or a playback device
/// is missing (CI, a non-Windows box).
/// </summary>
[NotInParallel] // SDL's subsystems start and stop on one thread at a time, or its audio state tears
public class SdlAudioDeviceTests
{
	[Test]
	public async Task The_default_device_asks_for_samples_in_real_time()
	{
		using var device = SdlAudioDevice.TryOpen(new AudioOptions(), out var reason)
			?? throw new TUnit.Core.Exceptions.SkipTestException($"No SDL audio device: {reason}");
		var renderer = new CountingRenderer();

		var clock = Stopwatch.StartNew();
		device.Start(renderer);
		await Task.Delay(600);
		device.Pause();
		var elapsed = clock.Elapsed.TotalSeconds;
		var frames = renderer.Frames;
		Console.WriteLine($"SDL '{device.Name}': {device.SampleRate} Hz, {device.BufferFrames}-frame buffer granted; " +
			$"{renderer.Callbacks} renders of {renderer.MinFrames}..{renderer.MaxFrames} frames, {frames} frames in {elapsed:0.000} s, " +
			$"on thread {renderer.Thread} (the test's: {Environment.CurrentManagedThreadId})");

		device.SampleRate.ShouldBeGreaterThan(0);
		renderer.Callbacks.ShouldBeGreaterThan(1);
		renderer.Thread.ShouldNotBe(Environment.CurrentManagedThreadId);
		// Real time, give or take the buffers queued at the start and the scheduler
		frames.ShouldBeInRange((long)(device.SampleRate * elapsed * 0.5), (long)(device.SampleRate * elapsed * 1.5) + 8192);
	}

	[Test]
	public void Closing_the_device_leaves_no_SDL_audio_running_and_it_reopens()
	{
		var first = SdlAudioDevice.TryOpen(new AudioOptions(), out var reason)
			?? throw new TUnit.Core.Exceptions.SkipTestException($"No SDL audio device: {reason}");
		first.Start(new CountingRenderer());
		first.Dispose();

		using var second = SdlAudioDevice.TryOpen(new AudioOptions(), out reason);

		second.ShouldNotBeNull(reason);
	}

	private sealed class CountingRenderer : IAudioRenderer
	{
		private long _frames;

		public long Frames => Interlocked.Read(ref _frames);
		public int Callbacks { get; private set; }
		public int MinFrames { get; private set; } = int.MaxValue;
		public int MaxFrames { get; private set; }
		public int Thread { get; private set; }

		public void Render(Span<float> output)
		{
			output.Clear();
			var n = output.Length / 2;
			Interlocked.Add(ref _frames, n);
			Callbacks++;
			MinFrames = Math.Min(MinFrames, n);
			MaxFrames = Math.Max(MaxFrames, n);
			Thread = Environment.CurrentManagedThreadId;
		}
	}
}
