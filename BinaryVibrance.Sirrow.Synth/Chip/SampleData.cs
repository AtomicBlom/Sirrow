namespace BinaryVibrance.Sirrow.Synth.Chip;

/// <summary>
/// A recording for a <see cref="OperatorKind.Sample"/> operator: mono samples at their own rate. One with a loop plays
/// to the loop's end and then round it for as long as the note is held, so a short recording sustains: a bowed note, an
/// organ pipe, a pad. <see cref="Looped"/> makes one whose loop joins without a click.
/// </summary>
/// <param name="Samples">The samples, -1..1.</param>
/// <param name="SampleRate">Their rate, in frames per second.</param>
public sealed record SampleData(float[] Samples, int SampleRate)
{
	/// <summary>The first frame of the loop, or -1 for a recording that plays once to its end. Set by <see cref="Looped"/>.</summary>
	public int LoopStart { get; private init; } = -1;

	/// <summary>The frame after the loop's last: reaching it, the playing goes back to <see cref="LoopStart"/>. Set by <see cref="Looped"/>.</summary>
	public int LoopEnd { get; private init; }

	/// <summary>Whether it has a loop.</summary>
	public bool IsLooped => LoopStart >= 0 && LoopEnd > LoopStart;

	// Whether the voice can play it: samples at a rate, and a loop (if any) inside them, at least two frames long. Checked
	// where a recording enters a chip (a SampleSet, a DrumKit), since a copy made with `with` skips Looped's checks
	internal void Check(string owner)
	{
		if (Samples is null || Samples.Length == 0)
			throw new ArgumentException($"{owner}: a recording has no samples.");
		if (SampleRate <= 0)
			throw new ArgumentException($"{owner}: a recording's rate is {SampleRate}, and must be more than 0.");
		if (LoopStart >= 0 && (LoopEnd < LoopStart + 2 || LoopEnd > Samples.Length))
			throw new ArgumentException(
				$"{owner}: a recording's loop runs from {LoopStart} to {LoopEnd}, which isn't two frames or more inside its {Samples.Length}.");
	}

	/// <summary>Decoded audio as a sample: stereo averaged to mono.</summary>
	public static SampleData From(AudioData data)
	{
		ArgumentNullException.ThrowIfNull(data);
		if (data.Channels == 1)
			return new SampleData(data.Samples, data.SampleRate);
		var mono = new float[data.Frames];
		for (var i = 0; i < mono.Length; i++)
			mono[i] = 0.5f * (data.Samples[2 * i] + data.Samples[2 * i + 1]);
		return new SampleData(mono, data.SampleRate);
	}

	/// <summary>
	/// This recording with a loop from <paramref name="loopStart"/> to <paramref name="loopEnd"/> (the frame after its
	/// last), joined so it doesn't click: over the loop's last <paramref name="crossfadeFrames"/> the recording fades out
	/// as what came just before the loop's start fades in, so the loop's end arrives exactly where its start sets off
	/// from. The samples are copied; this recording is left as it was.
	/// </summary>
	/// <remarks>
	/// The crossfade keeps the two at a constant sum, which suits a steady tone whose loop is a whole number of its
	/// periods long (the two are then the same wave, and the sum is that wave). Across a loop that isn't, the two cancel
	/// in part and the level dips for the length of the fade: choose the loop's ends a whole number of periods apart.
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The loop isn't inside the recording, is under two frames long, or the crossfade is longer than the loop or than
	/// what comes before it.
	/// </exception>
	public SampleData Looped(int loopStart, int loopEnd, int crossfadeFrames = 0)
	{
		if (loopStart < 0 || loopStart >= Samples.Length)
			throw new ArgumentOutOfRangeException(nameof(loopStart), loopStart, $"A loop starts inside the recording's {Samples.Length} frames.");
		if (loopEnd < loopStart + 2 || loopEnd > Samples.Length)
			throw new ArgumentOutOfRangeException(nameof(loopEnd), loopEnd, $"A loop ends at least two frames after it starts ({loopStart}) and no later than the recording does ({Samples.Length}).");
		if (crossfadeFrames < 0 || crossfadeFrames > loopStart || crossfadeFrames > loopEnd - loopStart)
			throw new ArgumentOutOfRangeException(nameof(crossfadeFrames), crossfadeFrames,
				$"A crossfade is no longer than the loop ({loopEnd - loopStart} frames) or than what comes before it ({loopStart}).");

		var samples = (float[])Samples.Clone();
		for (var i = 0; i < crossfadeFrames; i++)
		{
			var arriving = (i + 1f) / (crossfadeFrames + 1f);
			var at = loopEnd - crossfadeFrames + i;
			samples[at] = Samples[at] * (1f - arriving) + Samples[loopStart - crossfadeFrames + i] * arriving;
		}
		return this with { Samples = samples, LoopStart = loopStart, LoopEnd = loopEnd };
	}

	/// <summary>
	/// This recording of a steady note at <paramref name="hz"/> with a loop that starts <paramref name="startSeconds"/> in
	/// and is the whole number of the note's periods nearest <paramref name="lengthSeconds"/>, crossfaded over
	/// <paramref name="crossfadeSeconds"/>: the loop a held note wants, found from its pitch. See <see cref="Looped"/>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">The pitch isn't above zero, or the loop it makes doesn't fit (as <see cref="Looped"/>).</exception>
	public SampleData LoopedAt(double hz, double startSeconds, double lengthSeconds, double crossfadeSeconds = 0.02)
	{
		if (!double.IsFinite(hz) || hz <= 0)
			throw new ArgumentOutOfRangeException(nameof(hz), hz, "A note's pitch is above zero.");
		var start = (int)Math.Round(startSeconds * SampleRate);
		var periods = Math.Max(1, Math.Round(lengthSeconds * hz));
		var length = (int)Math.Round(periods * SampleRate / hz);
		var crossfade = Math.Min((int)Math.Round(crossfadeSeconds * SampleRate), Math.Min(start, length));
		return Looped(start, start + length, Math.Max(crossfade, 0));
	}
}
