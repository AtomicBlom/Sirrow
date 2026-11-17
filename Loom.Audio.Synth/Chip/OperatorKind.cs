namespace Loom.Audio.Synth.Chip;

/// <summary>What an operator of a <see cref="Patch"/> makes.</summary>
public enum OperatorKind
{
	/// <summary>A sine, which other operators can phase-modulate (FM), with feedback on itself.</summary>
	Sine,
	/// <summary>A band-limited sawtooth or pulse (<see cref="Waveform"/>).</summary>
	Wave,
	/// <summary>White or pink noise.</summary>
	Noise,
	/// <summary>Its envelope alone, as a level: a breath's pressure to drive a <see cref="Tube"/>.</summary>
	Level,
	/// <summary>A recorded sample, pitched to the note (<see cref="SampleSet"/>).</summary>
	Sample,
	/// <summary>A plucked string: a burst of noise, or its drivers, ringing round a tuned delay line.</summary>
	String,
	/// <summary>A flute: a jet driving a bore, blown by its drivers.</summary>
	Tube,
	/// <summary>A struck or rung body: a bank of resonances (<see cref="Mode"/>) rung by its drivers.</summary>
	Modes,
}
