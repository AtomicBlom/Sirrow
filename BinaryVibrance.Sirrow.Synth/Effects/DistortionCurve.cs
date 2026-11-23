namespace BinaryVibrance.Sirrow.Synth.Effects;

/// <summary>The shape a <see cref="Distortion"/> bends its input through.</summary>
public enum DistortionCurve
{
	/// <summary>tanh: rounds the peaks off smoothly, an overdriven valve.</summary>
	Soft,
	/// <summary>A cubic that meets a hard clip at ±1: harder edged, a transistor pushed past its rails.</summary>
	Hard,
	/// <summary>tanh pushed off centre, so the two halves clip differently: adds even harmonics, warmer.</summary>
	Asymmetric,
	/// <summary>Folds back past ±1 instead of flattening: bright and metallic, growing new harmonics with drive.</summary>
	Fold,
}
