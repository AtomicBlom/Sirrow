using System.Runtime.CompilerServices;

namespace Loom.Audio.Synth.Dsp;

/// <summary>
/// A note's level over time, by an <see cref="EnvelopeShape"/>: a straight attack, then exponential decay and release,
/// so each falls by 60 dB over its time. Gate it on and off; read it once a sample.
/// </summary>
public struct Envelope
{
	private enum Stage : byte { Idle, Attack, Decay, Release }

	private Stage _stage;
	private float _level;
	private float _attackStep, _decayFactor, _releaseFactor, _sustain;

	/// <summary>Works out the per-sample steps of <paramref name="shape"/> at <paramref name="rate"/>. Call before gating it.</summary>
	public void Prepare(in EnvelopeShape shape, int rate)
	{
		_attackStep = 1f / (MathF.Max(shape.Attack, 0.0005f) * rate);
		_decayFactor = MathF.Exp(-6.9078f / (MathF.Max(shape.Decay, 0.0005f) * rate));
		_releaseFactor = MathF.Exp(-6.9078f / (MathF.Max(shape.Release, 0.0005f) * rate));
		_sustain = Math.Clamp(shape.Sustain, 0f, 1f);
	}

	/// <summary>The level now, 0 to 1.</summary>
	public readonly float Level => _level;

	/// <summary>Whether it has died away and gives silence until gated on again.</summary>
	public readonly bool IsIdle => _stage == Stage.Idle;

	/// <summary>On starts the attack from the level now, so a note struck again doesn't click; off starts the release from it.</summary>
	public void Gate(bool on) => _stage = on ? Stage.Attack : _level > 0f ? Stage.Release : Stage.Idle;

	/// <summary>The next sample's level.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Next()
	{
		switch (_stage)
		{
			case Stage.Attack:
				_level += _attackStep;
				if (_level >= 1f)
				{
					_level = 1f;
					_stage = Stage.Decay;
				}
				break;
			case Stage.Decay:
				_level = _sustain + (_level - _sustain) * _decayFactor;
				break;
			case Stage.Release:
				_level *= _releaseFactor;
				if (_level < 1e-4f)
				{
					_level = 0f;
					_stage = Stage.Idle;
				}
				break;
			default:
				_level = 0f;
				break;
		}
		return _level;
	}
}
