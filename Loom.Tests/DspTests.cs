using Loom.Audio.Synth;
using Loom.Audio.Synth.Dsp;

namespace Loom.Tests;

/// <summary>The DSP building blocks, run through plain loops at 48 kHz and measured against what each should do.</summary>
public class DspTests
{
	private const int Rate = 48_000;

	private static float[] Sine(double hz, int length, float amplitude = 1f)
	{
		var samples = new float[length];
		for (var n = 0; n < length; n++)
			samples[n] = (float)(amplitude * Math.Sin(2 * Math.PI * hz * n / Rate));
		return samples;
	}

	private static float[] Through(float[] input, Func<float, float> filter)
	{
		var output = new float[input.Length];
		for (var n = 0; n < input.Length; n++)
			output[n] = filter(input[n]);
		return output;
	}

	/// <summary>The gain in decibels a filter gives a sine at <paramref name="hz"/>, read after half a second of settling.</summary>
	private static float GainDb(double hz, Func<float, float> filter)
	{
		var output = Through(Sine(hz, Rate * 2), filter);
		return AudioAnalysis.ToDb(AudioAnalysis.LevelAt(output.AsSpan(Rate / 2), hz, Rate));
	}

	private static float BandEnergy(float[] spectrum, double fromHz, double toHz, int length)
	{
		var binHz = (double)Rate / length;
		var energy = 0.0;
		for (var k = (int)Math.Ceiling(fromHz / binHz); k <= (int)(toHz / binHz); k++)
			energy += spectrum[k] * (double)spectrum[k];
		return (float)energy;
	}

	[Test]
	public void Units_convert()
	{
		Units.NoteToHz(69).ShouldBe(440f, 1e-3f);
		Units.NoteToHz(60).ShouldBe(261.626f, 0.01f);
		Units.HzToNote(440).ShouldBe(69f, 1e-4f);
		Units.CentsToRatio(1200).ShouldBe(2f, 1e-5f);
		Units.DbToGain(-6.0206f).ShouldBe(0.5f, 1e-4f);
		Units.GainToDb(0.5f).ShouldBe(-6.0206f, 1e-3f);
	}

	[Test]
	public void The_sine_table_matches_the_sine()
	{
		for (var i = 0; i < 1000; i++)
		{
			var phase = -2f + 5f * i / 999f;
			SineTable.At(phase).ShouldBe((float)Math.Sin(2 * Math.PI * phase), 1e-5f);
		}
	}

	[Test]
	public void A_polyblep_saw_has_its_fundamental_and_far_less_aliasing_than_a_naive_one()
	{
		// At 2100 Hz the wave doesn't repeat in a whole number of samples, so its harmonics past the rate fold onto
		// frequencies that are none of its own: the 23rd, at 48 300 Hz, folds to 300 Hz.
		const double hz = 2100;
		const float dt = (float)(hz / Rate);
		var blep = new float[Rate];
		var naive = new float[Rate];
		var phase = 0f;
		for (var n = 0; n < Rate; n++)
		{
			blep[n] = PolyBlep.Saw(phase, dt);
			naive[n] = 2f * phase - 1f;
			phase += dt;
			phase -= MathF.Floor(phase);
		}

		AudioAnalysis.LevelAt(blep, hz, Rate).ShouldBe(0.6366f, 0.03f);
		AudioAnalysis.LevelAt(naive, 300, Rate).ShouldBeGreaterThan(0.003f);
		AudioAnalysis.LevelAt(blep, 300, Rate).ShouldBeLessThan(0.003f);
	}

	[Test]
	public void A_polyblep_square_has_a_square_fundamental()
	{
		const double hz = 1000;
		const float dt = (float)(hz / Rate);
		var square = new float[Rate];
		var phase = 0f;
		for (var n = 0; n < Rate; n++)
		{
			square[n] = PolyBlep.Square(phase, dt, 0.5f);
			phase += dt;
			phase -= MathF.Floor(phase);
		}

		AudioAnalysis.LevelAt(square, hz, Rate).ShouldBe(1.2732f, 0.03f);
		AudioAnalysis.LevelAt(square, 2 * hz, Rate).ShouldBeLessThan(0.01f);
	}

	[Test]
	public void Noise_repeats_from_its_seed_and_stays_in_range()
	{
		var a = new Noise(1234);
		var b = new Noise(1234);
		var sum = 0.0;
		for (var i = 0; i < 1000; i++)
		{
			var x = a.Next();
			b.Next().ShouldBe(x);
			x.ShouldBeGreaterThanOrEqualTo(-1f);
			x.ShouldBeLessThan(1f);
			sum += x;
		}
		(sum / 1000).ShouldBe(0, 0.1);
	}

	[Test]
	public void Pink_noise_has_the_same_energy_in_each_octave()
	{
		var noise = new Noise(7);
		var pink = new PinkFilter();
		var samples = new float[Rate * 10];
		for (var n = 0; n < samples.Length; n++)
			samples[n] = pink.Tick(noise.Next());

		var spectrum = AudioAnalysis.Spectrum(samples);
		var length = (spectrum.Length - 1) * 2;
		var low = BandEnergy(spectrum, 200, 400, length);
		var high = BandEnergy(spectrum, 3200, 6400, length);

		Math.Abs(10 * Math.Log10(low / high)).ShouldBeLessThan(3);
	}

	[Test]
	public void The_svf_low_pass_passes_below_its_cutoff_and_cuts_above()
	{
		float Gain(double hz)
		{
			var svf = new Svf();
			svf.Set(1000, 0.707f, Rate);
			return GainDb(hz, svf.LowPass);
		}

		Gain(100).ShouldBe(0f, 0.5f);
		Gain(1000).ShouldBe(-3f, 0.5f);
		Gain(10_000).ShouldBeLessThan(-36f);
	}

	[Test]
	public void The_svf_band_pass_is_unity_at_its_centre_whatever_the_q()
	{
		float Gain(double hz)
		{
			var svf = new Svf();
			svf.Set(1000, 8, Rate);
			return GainDb(hz, svf.BandPass);
		}

		Gain(1000).ShouldBe(0f, 0.5f);
		Gain(500).ShouldBeLessThan(-15f);
	}

	[Test]
	public void The_svf_stays_finite_while_its_cutoff_sweeps()
	{
		var svf = new Svf();
		var noise = new Noise(3);
		const int block = 256;
		for (var b = 0; b < Rate * 2 / block; b++)
		{
			var t = (b % 100) / 50f;
			var hz = 50f * MathF.Pow(300f, t <= 1 ? t : 2 - t);
			svf.Set(hz, 4, Rate);
			for (var n = 0; n < block; n++)
				float.IsFinite(svf.LowPass(noise.Next())).ShouldBeTrue();
			svf.Flush();
		}
	}

	[Test]
	public void A_biquad_peak_raises_its_band_and_leaves_the_rest()
	{
		float Gain(double hz)
		{
			var peak = new Biquad();
			peak.SetPeak(1000, 1, 6, Rate);
			return GainDb(hz, peak.Tick);
		}

		Gain(1000).ShouldBe(6f, 0.3f);
		Gain(100).ShouldBe(0f, 0.3f);
	}

	[Test]
	public void Biquad_shelves_and_passes_move_the_right_end()
	{
		float Gain(double hz, Action<Biquad[]> set)
		{
			var filter = new Biquad[1];
			set(filter);
			return GainDb(hz, x => filter[0].Tick(x));
		}

		Gain(50, f => f[0].SetLowShelf(500, 6, Rate)).ShouldBe(6f, 0.5f);
		Gain(10_000, f => f[0].SetLowShelf(500, 6, Rate)).ShouldBe(0f, 0.5f);
		Gain(15_000, f => f[0].SetHighShelf(2000, -6, Rate)).ShouldBe(-6f, 0.5f);
		Gain(100, f => f[0].SetHighShelf(2000, -6, Rate)).ShouldBe(0f, 0.5f);
		Gain(1000, f => f[0].SetLowPass(1000, 0.707f, Rate)).ShouldBe(-3f, 0.5f);
		Gain(1000, f => f[0].SetHighPass(1000, 0.707f, Rate)).ShouldBe(-3f, 0.5f);
	}

	[Test]
	public void A_delay_line_reads_back_whole_and_between()
	{
		var line = new DelayLine(64);
		for (var i = 1; i <= 100; i++)
			line.Write(i);

		line.Read(1).ShouldBe(100f);
		line.Read(10).ShouldBe(91f);
		line.ReadHermite(10f).ShouldBe(line.Read(10));
		line.ReadHermite(10.5f).ShouldBe(90.5f, 1e-3f);
	}

	[Test]
	public void An_envelope_attacks_decays_holds_and_releases_on_time()
	{
		var envelope = new Envelope();
		envelope.Prepare(EnvelopeShape.Adsr(0.01f, 0.1f, 0.5f, 0.2f), Rate);
		envelope.Gate(true);

		var reachedFull = -1;
		var sample = 0;
		for (; sample < (int)(0.11 * Rate); sample++)
		{
			if (envelope.Next() >= 1f && reachedFull < 0)
				reachedFull = sample + 1;
		}
		reachedFull.ShouldBeInRange(479, 481);
		envelope.Level.ShouldBe(0.5f, 0.001f);

		for (; sample < (int)(0.3 * Rate); sample++)
			envelope.Next();
		envelope.Gate(false);
		for (; sample < (int)(0.5 * Rate); sample++)
			envelope.Next();
		envelope.Level.ShouldBeLessThan(0.5f * 0.0011f);

		for (; sample < (int)(0.6 * Rate); sample++)
			envelope.Next();
		envelope.IsIdle.ShouldBeTrue();
	}

	[Test]
	public void A_struck_resonator_rings_at_its_pitch_and_dies_on_time()
	{
		var resonator = new Resonator();
		resonator.Set(440, 1, Rate);
		var ring = new float[Rate * 2];
		ring[0] = resonator.Tick(1f);
		for (var n = 1; n < ring.Length; n++)
			ring[n] = resonator.Tick(0f);

		AudioAnalysis.PeakFrequency(ring, Rate, 100, 2000).ShouldBe(440, 1);
		AudioAnalysis.Peak(ring.AsSpan(0, Rate / 20)).ShouldBe(1f, 0.2f);
		AudioAnalysis.Peak(ring.AsSpan(Rate, Rate / 20)).ShouldBeLessThan(0.0015f);
	}

	[Test]
	public void An_lfo_runs_at_its_rate()
	{
		var lfo = new Lfo();
		lfo.Set(5, Rate);
		var wave = new float[Rate];
		for (var n = 0; n < wave.Length; n++)
			wave[n] = lfo.Next();

		AudioAnalysis.LevelAt(wave, 5, Rate).ShouldBe(1f, 0.01f);
	}

	private static float[] UpThenDown(int factor, double hz)
	{
		const int block = 256;
		var oversampler = new Oversampler(factor, block);
		var input = Sine(hz, block * 187);
		var raised = new float[block * factor];
		var output = new float[input.Length];
		for (var start = 0; start < input.Length; start += block)
		{
			oversampler.Up(input.AsSpan(start, block), raised);
			oversampler.Down(raised, output.AsSpan(start, block));
		}
		return output;
	}

	[Test]
	public void Oversampling_up_and_down_returns_the_signal()
	{
		AudioAnalysis.ToDb(AudioAnalysis.LevelAt(UpThenDown(4, 1000).AsSpan(200), 1000, Rate)).ShouldBe(0f, 0.05f);
		AudioAnalysis.ToDb(AudioAnalysis.LevelAt(UpThenDown(4, 18_000).AsSpan(200), 18_000, Rate)).ShouldBe(0f, 0.5f);
		AudioAnalysis.ToDb(AudioAnalysis.LevelAt(UpThenDown(2, 1000).AsSpan(200), 1000, Rate)).ShouldBe(0f, 0.05f);
	}

	[Test]
	public void Oversampling_delays_by_a_whole_number_of_frames()
	{
		foreach (var factor in new[] { 2, 4 })
		{
			const int block = 256;
			var oversampler = new Oversampler(factor, block);
			var input = new float[block * 2];
			input[block - 10] = 1f; // across a block's edge, where the 4× path's carried sample is
			var raised = new float[block * factor];
			var output = new float[input.Length];
			for (var start = 0; start < input.Length; start += block)
			{
				oversampler.Up(input.AsSpan(start, block), raised);
				oversampler.Down(raised, output.AsSpan(start, block));
			}

			var at = block - 10 + oversampler.LatencyFrames;
			for (var i = 0; i < output.Length; i++)
				MathF.Abs(output[i]).ShouldBeLessThanOrEqualTo(MathF.Abs(output[at]), $"factor {factor}, frame {i}");
			// A linear-phase response sampled at its centre is symmetric about it
			for (var k = 1; k < 30; k++)
				output[at - k].ShouldBe(output[at + k], 1e-6f, $"factor {factor}, {k} either side");
		}
	}

	[Test]
	public void Oversampling_up_leaves_no_image()
	{
		const int block = 256;
		var oversampler = new Oversampler(2, block);
		var input = Sine(1000, block * 187);
		var raised = new float[input.Length * 2];
		for (var start = 0; start < input.Length; start += block)
			oversampler.Up(input.AsSpan(start, block), raised.AsSpan(start * 2, block * 2));

		var signal = AudioAnalysis.LevelAt(raised, 1000, Rate * 2);
		var image = AudioAnalysis.LevelAt(raised, 47_000, Rate * 2);

		AudioAnalysis.ToDb(image / signal).ShouldBeLessThan(-60f);
	}

	[Test]
	public void An_oversampler_takes_only_2_or_4()
	{
		Should.Throw<ArgumentOutOfRangeException>(() => new Oversampler(3));
	}

	[Test]
	public void The_per_sample_methods_allocate_nothing()
	{
		var noise = new Noise(1);
		var pink = new PinkFilter();
		var onePole = new OnePole();
		onePole.Set(500, Rate);
		var svf = new Svf();
		svf.Set(1000, 1, Rate);
		var biquad = new Biquad();
		biquad.SetPeak(1000, 1, 3, Rate);
		var envelope = new Envelope();
		envelope.Prepare(EnvelopeShape.Adsr(0.01f, 0.1f, 0.5f, 0.2f), Rate);
		envelope.Gate(true);
		var lfo = new Lfo();
		lfo.Set(3, Rate);
		var resonator = new Resonator();
		resonator.Set(440, 1, Rate);
		var line = new DelayLine(100);
		var sum = 0f;

		void Run(int count)
		{
			for (var i = 0; i < count; i++)
			{
				var x = noise.Next();
				sum += pink.Tick(x) + onePole.LowPass(x) + svf.BandPass(x) + biquad.Tick(x) + envelope.Next() + lfo.Next()
					+ resonator.Tick(x) + SineTable.At(x) + PolyBlep.Saw(0.5f + 0.5f * x, 0.01f)
					+ PolyBlep.Square(0.5f + 0.5f * x, 0.01f, 0.5f) + line.ReadHermite(10.5f);
				line.Write(x);
			}
		}

		Run(1000);
		var before = GC.GetAllocatedBytesForCurrentThread();
		Run(100_000);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		allocated.ShouldBe(0);
		float.IsFinite(sum).ShouldBeTrue();
	}

	[Test]
	public void A_struck_envelope_falls_to_zero_and_goes_idle()
	{
		// Dying 60 dB over 0.1 s, it passes 80 dB down at a third past that: by twice its time it is exactly zero and idle,
		// sample by sample and a block at a time alike
		var one = new Envelope();
		one.Prepare(EnvelopeShape.Struck(0.1f), Rate);
		one.Gate(true);
		for (var i = 0; i < Rate / 5; i++)
			one.Next();
		one.IsIdle.ShouldBeTrue();
		one.Level.ShouldBe(0f);

		var block = new Envelope();
		block.Prepare(EnvelopeShape.Struck(0.1f), Rate);
		block.Gate(true);
		var levels = new float[256];
		for (var i = 0; i < Rate / 5 / 256; i++)
			block.Fill(levels);
		block.IsIdle.ShouldBeTrue();
		levels.ShouldAllBe(l => l == 0f);
	}

	[Test]
	public void An_envelope_that_sustains_never_goes_idle_while_held()
	{
		var envelope = new Envelope();
		envelope.Prepare(EnvelopeShape.Adsr(0.01f, 0.05f, 0.001f, 0.2f), Rate);
		envelope.Gate(true);
		for (var i = 0; i < Rate; i++)
			envelope.Next();
		envelope.IsIdle.ShouldBeFalse();
		envelope.Level.ShouldBe(0.001f, 1e-6f);
	}
}
