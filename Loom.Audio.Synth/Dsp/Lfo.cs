using System.Runtime.CompilerServices;

namespace Loom.Audio.Synth.Dsp;

/// <summary>A slow sine for moving something else: a vibrato, a tremolo, a squeal's wobble. Step it per sample or per block.</summary>
public struct Lfo
{
	private float _phase;
	private float _step;

	/// <summary>Sets its rate, in cycles per <see cref="Next"/> call at <paramref name="rate"/> calls a second.</summary>
	public void Set(float hz, int rate) => _step = hz / rate;

	/// <summary>Moves it to <paramref name="phase"/>, in cycles.</summary>
	public void Reset(float phase = 0f) => _phase = phase - MathF.Floor(phase);

	/// <summary>Its value now, −1 to 1, then a step on.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Next()
	{
		var value = SineTable.At(_phase);
		_phase += _step;
		_phase -= MathF.Floor(_phase);
		return value;
	}
}
