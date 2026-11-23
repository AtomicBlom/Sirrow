using BinaryVibrance.Sirrow.Synth.Dsp;

namespace BinaryVibrance.Sirrow.Synth.Effects;

/// <summary>
/// Four bands of tone control: a low shelf, two peaks and a high shelf, each 0 dB (flat) until set. A band's filters are
/// worked out again only in a block where one of its values changed.
/// </summary>
public sealed class Eq : AudioEffect
{
	private readonly Biquad[] _left = new Biquad[4];
	private readonly Biquad[] _right = new Biquad[4];
	private readonly float[] _last = new float[10];
	// A band at 0 dB is flat, as near as its filter's rounding goes: it is skipped, and its filters start afresh when it
	// is set again
	private readonly bool[] _flat = new bool[4];

	/// <summary>The low shelf's corner, in hertz. Default 120.</summary>
	public AudioParameter LowHz { get; } = new(120f);

	/// <summary>The low shelf's gain, in decibels. Default 0.</summary>
	public AudioParameter LowDb { get; } = new(0f);

	/// <summary>The first peak's centre, in hertz. Default 500.</summary>
	public AudioParameter Peak1Hz { get; } = new(500f);

	/// <summary>The first peak's sharpness: higher is narrower. Default 1.</summary>
	public AudioParameter Peak1Q { get; } = new(1f);

	/// <summary>The first peak's gain, in decibels. Default 0.</summary>
	public AudioParameter Peak1Db { get; } = new(0f);

	/// <summary>The second peak's centre, in hertz. Default 3000.</summary>
	public AudioParameter Peak2Hz { get; } = new(3000f);

	/// <summary>The second peak's sharpness: higher is narrower. Default 1.</summary>
	public AudioParameter Peak2Q { get; } = new(1f);

	/// <summary>The second peak's gain, in decibels. Default 0.</summary>
	public AudioParameter Peak2Db { get; } = new(0f);

	/// <summary>The high shelf's corner, in hertz. Default 8000.</summary>
	public AudioParameter HighHz { get; } = new(8000f);

	/// <summary>The high shelf's gain, in decibels. Default 0.</summary>
	public AudioParameter HighDb { get; } = new(0f);

	/// <inheritdoc/>
	protected override void OnPrepare(int sampleRate)
	{
		Array.Fill(_last, float.NaN);
		Configure();
	}

	/// <inheritdoc/>
	public override void Process(Span<float> left, Span<float> right)
	{
		Configure();
		for (var b = 0; b < 4; b++)
		{
			if (_flat[b])
				continue;
			ref var l = ref _left[b];
			ref var r = ref _right[b];
			for (var s = 0; s < left.Length; s++)
			{
				left[s] = l.Tick(left[s]);
				right[s] = r.Tick(right[s]);
			}
			l.Flush();
			r.Flush();
		}
	}

	// Each band's filters set again when one of its values differs from the last block's
	private void Configure()
	{
		var rate = SampleRate;
		if (Changed(0, LowHz.Snap()) | Changed(1, LowDb.Snap()))
			Both(0).SetLowShelf(_last[0], _last[1], rate);
		if (Changed(2, Peak1Hz.Snap()) | Changed(3, Peak1Q.Snap()) | Changed(4, Peak1Db.Snap()))
			Both(1).SetPeak(_last[2], _last[3], _last[4], rate);
		if (Changed(5, Peak2Hz.Snap()) | Changed(6, Peak2Q.Snap()) | Changed(7, Peak2Db.Snap()))
			Both(2).SetPeak(_last[5], _last[6], _last[7], rate);
		if (Changed(8, HighHz.Snap()) | Changed(9, HighDb.Snap()))
			Both(3).SetHighShelf(_last[8], _last[9], rate);

		Flat(0, _last[1]);
		Flat(1, _last[4]);
		Flat(2, _last[7]);
		Flat(3, _last[9]);
	}

	// Whether a band at `db` is skipped; one coming back from flat starts with empty filters, not what it held before
	private void Flat(int band, float db)
	{
		var flat = db == 0f;
		if (_flat[band] && !flat)
		{
			_left[band].Reset();
			_right[band].Reset();
		}
		_flat[band] = flat;
	}

	private bool Changed(int index, float value)
	{
		if (_last[index] == value)
			return false;
		_last[index] = value;
		return true;
	}

	private BandSetter Both(int band) => new(this, band);

	// Sets one band's filter on both sides alike, keeping each side's own state
	private readonly struct BandSetter(Eq eq, int band)
	{
		public void SetLowShelf(float hz, float db, int rate)
		{
			eq._left[band].SetLowShelf(hz, db, rate);
			eq._right[band].SetLowShelf(hz, db, rate);
		}

		public void SetPeak(float hz, float q, float db, int rate)
		{
			eq._left[band].SetPeak(hz, q, db, rate);
			eq._right[band].SetPeak(hz, q, db, rate);
		}

		public void SetHighShelf(float hz, float db, int rate)
		{
			eq._left[band].SetHighShelf(hz, db, rate);
			eq._right[band].SetHighShelf(hz, db, rate);
		}
	}
}
