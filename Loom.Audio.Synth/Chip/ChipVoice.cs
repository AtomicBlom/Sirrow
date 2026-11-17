using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Chip;

/// <summary>
/// One voice of a chip: a note of a patch, its operators' states, its strings, tube, filter and vibrato. Everything is
/// allocated when the voice is made; starting, releasing and rendering a note allocate nothing.
/// </summary>
internal sealed class ChipVoice
{
	private const int LineFrames = 4096;
	private const float TwoPi = 2f * MathF.PI;
	private const float Silent = 1e-4f;
	// The tube's drivers scaled to the pressure the STK's flute is blown at (1.1 to 1.3), so a breath of 0.8 to 0.9 at its
	// sustain, as a patch writes it, speaks: under about 1 the jet never starts and the bore only rings with the noise
	private const float TubePressure = 1.4f;

	private readonly int _rate;
	private readonly OpState[] _ops = new OpState[Patch.MaxOperators];
	private readonly bool[] _idle = new bool[Patch.MaxOperators];
	private readonly float[] _env = new float[AudioEffect.MaxBlockFrames];   // one operator's envelope times its level, a block at a time
	private readonly float[] _mix = new float[AudioEffect.MaxBlockFrames];   // the heard operators, mixed
	private readonly float[] _mixRight = new float[AudioEffect.MaxBlockFrames];   // and the right of them, for a patch whose operators are heard apart
	private readonly float[] _tapLeft = new float[Patch.MaxOutputs];   // each heard operator's level to the left and to the right, for such a patch
	private readonly float[] _tapRight = new float[Patch.MaxOutputs];
	private Svf _filterRight;
	private float _filterCutoff;
	private readonly int[] _stringSlot = new int[Patch.MaxOperators];
	private readonly DelayLine[] _strings = [new(LineFrames), new(LineFrames)];
	private readonly DelayLine _jet = new(LineFrames);
	private readonly DelayLine _bore = new(LineFrames);
	private readonly float[] _pluck = new float[LineFrames];       // a pluck being shaped, before it goes into its string
	private readonly Resonator[] _modes = new Resonator[Patch.MaxOperators * Mode.Max];   // each operator's modes, in a row
	private float _reflect, _dc, _dcIn, _boreOut;
	private Svf _filter;
	private Envelope _filterEnvelope;
	private Lfo _lfo;
	private Lfo _tremolo;
	private float _attackFrames;

	// How often the pitch, the filter and the levels are worked out, in frames: often enough that a drum's falling pitch
	// and a filter's sweep are heard as moving, not as steps
	private const int ControlFrames = 64;

	public ChipVoice(int index, int rate)
	{
		_rate = rate;
		for (var i = 0; i < _ops.Length; i++)
			_ops[i].Noise = new Noise((uint)(index * Patch.MaxOperators + i + 1));
	}

	public bool Active;
	public ChipInstrumentState? Instrument;
	public Patch Patch = null!;
	public int NoteId;
	public float TargetNote;
	public float CurrentNote;
	public float Velocity;
	public float NotePan;
	public long Order;            // when it started, so the oldest can be stolen
	public bool Released;
	public long ReleasedOrder;    // when it was released, so the one released longest can be stolen
	public int GateFrames;        // frames until it gates off; below 0 for none
	public int AgeFrames;
	public float Fade = 1f;
	public float FadeStep;        // above 0 while fading out: stolen, or choked
	public int PieceIndex = -1;
	public int ChokeGroup;
	public SampleData? Take;      // a kit hit's take, for its one sample operator
	public int InstrumentIndex;   // the instrument's slot in the core
	public int GainSlot;          // 1 + a slot of the core's GainSlots, or 0 for none
	public float SlotGain;        // the slot's value the last block used; below 0 before the first
	public float LastPeak;        // the loudest of its last block, after its gain, before pan and the group's effects

	public bool HasPending;
	public ChipEvent Pending;     // the note a stolen voice starts once faded
	public SampleData? PendingTake;

	public int OperatorCount => Patch.Operators.Length;

	/// <summary>Starts a note: every operator from the beginning, envelopes gated on, strings and tube cleared.</summary>
	public void Start(ChipInstrumentState instrument, Patch patch, in ChipEvent e, int gateFrames, SampleData? take, long order)
	{
		Active = true;
		Instrument = instrument;
		Patch = patch;
		NoteId = e.NoteId;
		TargetNote = CurrentNote = e.Note;
		Velocity = e.Velocity;
		NotePan = e.Pan;
		Order = order;
		Released = false;
		GateFrames = gateFrames;
		AgeFrames = 0;
		Fade = 1f;
		FadeStep = 0f;
		HasPending = false;
		PieceIndex = e.Kind == ChipEventKind.Hit ? e.PieceIndex : -1;
		ChokeGroup = e.Kind == ChipEventKind.Hit && instrument.Kit is { } kit ? kit.Pieces[e.PieceIndex].ChokeGroup : 0;
		Take = take;
		InstrumentIndex = e.Instrument;
		GainSlot = e.GainSlot;
		SlotGain = -1f;
		LastPeak = 0f;

		var hz = Units.NoteToHz(e.Note);
		var strings = 0;
		_attackFrames = 0f;
		var specs = patch.Operators;
		for (var i = 0; i < specs.Length; i++)
		{
			var spec = specs[i];
			ref var op = ref _ops[i];
			op.Phase = spec.StartPhase;
			op.Y1 = op.Y2 = 0f;
			// How the operator follows the keyboard: its level by decibels an octave, its ring by a share an octave
			var octaves = (e.Note - 60f) / 12f;
			op.KeyGain = spec.KeyLevelDb == 0f ? 1f : Units.DbToGain(spec.KeyLevelDb * octaves);
			op.KeyTime = spec.KeyDecay == 0f ? 1f : MathF.Pow(MathF.Max(1f - Math.Clamp(spec.KeyDecay, 0f, 0.95f), 0.05f), octaves);
			var shape = spec.Envelope;
			if (op.KeyTime != 1f)
				shape = shape with { Decay = shape.Decay * op.KeyTime, Release = shape.Release * op.KeyTime };
			op.Env.Prepare(shape, _rate);
			op.Env.Gate(true);
			op.Pink = default;
			op.Position = 0.0;
			op.Data = null;
			op.RootHz = 0f;
			op.Previous = 0f;
			op.Burst = 0;
			op.LossScale = 1f;
			_attackFrames = MathF.Max(_attackFrames, spec.Envelope.Attack * _rate);
			switch (spec.Kind)
			{
				case OperatorKind.Sample:
					if (take is not null)
					{
						op.Data = take;
						op.RootHz = hz;
					}
					else
					{
						var zone = spec.Samples!.Pick((int)MathF.Round(e.Note), e.Velocity, instrument.NoteTurn);
						op.Data = zone.Data;
						op.RootHz = Units.NoteToHz(zone.RootNote);
					}
					break;
				case OperatorKind.String:
					_stringSlot[i] = strings;
					_strings[strings].Clear();
					if (spec.Drivers.Length == 0)
						Pluck(_strings[strings], ref op, spec, hz, e.Velocity);
					strings++;
					break;
				case OperatorKind.Modes:
					Array.Clear(_modes, i * Mode.Max, Mode.Max);
					break;
				case OperatorKind.Tube:
					_jet.Clear();
					_bore.Clear();
					_reflect = _dc = _dcIn = _boreOut = 0f;
					break;
			}
		}
		instrument.NoteTurn++;

		_filter = default;
		_filterRight = default;
		if (patch.Wide)
			for (var t = 0; t < patch.Outputs.Length; t++)
			{
				// Each heard operator between the two sides by its pan, at the same power wherever it sits
				var tap = patch.Outputs[t];
				var across = (Math.Clamp(tap.Pan, -1f, 1f) + 1f) * (MathF.PI / 4f);
				_tapLeft[t] = tap.Level * MathF.Cos(across);
				_tapRight[t] = tap.Level * MathF.Sin(across);
			}
		if (patch.Filter is { } filter)
		{
			_filterEnvelope.Prepare(filter.Envelope, _rate);
			_filterEnvelope.Gate(true);
		}
		_lfo.Reset(0f);
		_tremolo.Reset(0f);
	}

	// A pluck: one period of the shape a plucked string starts from, put into the string. Pulled aside at one point and
	// let go, a string is a triangle with its peak there, which has every harmonic but those with a still point at the
	// pick, each weaker as the square of its number: a soft, round pluck. A harder one is nearer that triangle's slope (a
	// step up, a step down), whose harmonics fall only as their number: brighter. A little noise on top keeps two plucks
	// from being the same sample for sample. Noise alone, which a simpler string starts from, has harmonics of whatever
	// strength it happened to have, so over the few samples of a high note's period one pluck was loud and the next thin
	private void Pluck(DelayLine line, ref OpState op, OperatorSpec spec, float hz, float velocity)
	{
		var period = Math.Clamp((int)MathF.Ceiling(_rate / (hz * spec.Ratio)), 2, LineFrames - 8);
		var shaped = _pluck.AsSpan(0, period);
		var pick = Math.Clamp(spec.Pick, 0.02f, 0.5f);
		var hard = 0.25f + 0.75f * Math.Clamp(velocity, 0f, 1f);
		// The triangle and its slope, each with no average and a strength of 1
		var triangle = (1f - hard) * 3.4641f;
		var step = hard * MathF.Sqrt(pick * (1f - pick));
		var squares = 0f;
		for (var s = 0; s < period; s++)
		{
			var t = (s + 0.5f) / period;
			var x = t < pick
				? triangle * (t / pick - 0.5f) + step / pick
				: triangle * ((1f - t) / (1f - pick) - 0.5f) - step / (1f - pick);
			x += 0.25f * op.Noise.Next();
			shaped[s] = x;
			squares += x * x;
		}
		// To one strength whatever the shape, at the pluck's velocity. White noise at full scale is more than twice as
		// strong, but most of it is high harmonics that die at once or never pass a patch's filter: this sounds as loud
		var scale = squares > 1e-12f ? 0.26f * velocity / MathF.Sqrt(squares / period) : 0f;
		for (var s = 0; s < period; s++)
			line.Write(shaped[s] * scale);
		op.Burst = 0;
	}

	/// <summary>A note moved by a mono patch: the new pitch and id, its envelopes left alone (gated on again if let go).</summary>
	public void Move(in ChipEvent e, int gateFrames)
	{
		TargetNote = e.Note;
		NoteId = e.NoteId;
		GateFrames = gateFrames;
		if (!Released)
			return;
		Released = false;
		for (var i = 0; i < Patch.Operators.Length; i++)
		{
			_ops[i].Env.Gate(true);
			_ops[i].LossScale = 1f;
		}
		_filterEnvelope.Gate(true);
	}

	/// <summary>Lets the note go: every envelope gated off (a one-shot sample plays on), a string damped.</summary>
	public void Release(long order)
	{
		if (Released)
			return;
		Released = true;
		ReleasedOrder = order;
		GateFrames = -1;
		var specs = Patch.Operators;
		for (var i = 0; i < specs.Length; i++)
		{
			var spec = specs[i];
			if (spec.Kind == OperatorKind.Sample && spec.OneShot)
				continue;
			_ops[i].Env.Gate(false);
			if (spec.Kind == OperatorKind.String)
				_ops[i].LossScale = 0.9f;
		}
		_filterEnvelope.Gate(false);
	}

	/// <summary>
	/// Renders <paramref name="n"/> frames into its group's buffers and the sends'; returns whether the note has ended
	/// (its heard operators idle and the block silent, or faded out). A note played for a time is let go at its frame,
	/// not at the block's start: the stretch is cut there and <paramref name="released"/> numbers its release.
	/// </summary>
	public bool Render(int n, float[][] scratch, float[] groupLeft, float[] groupRight, float[] spaceLeft, float[] spaceRight,
		float[] echoLeft, float[] echoRight, float[] keyLeft, float[] keyRight, float[] gainSlots, ref long released)
	{
		LastPeak = 0f;
		var ended = false;
		for (var offset = 0; offset < n;)
		{
			var frames = Math.Min(ControlFrames, n - offset);
			if (GateFrames > 0 && GateFrames < frames)
				frames = GateFrames;
			ended = RenderStretch(offset, frames, scratch, groupLeft, groupRight, spaceLeft, spaceRight,
				echoLeft, echoRight, keyLeft, keyRight, gainSlots);
			offset += frames;
			if (GateFrames > 0)
			{
				GateFrames -= frames;
				if (GateFrames <= 0)
					Release(++released);
			}
		}
		return ended;
	}

	// The next n frames, at most ControlFrames, into the buffers from offset on: the pitch, the filter and the levels
	// worked out once, then each operator in turn
	private bool RenderStretch(int offset, int n, float[][] scratch, float[] groupLeft, float[] groupRight, float[] spaceLeft, float[] spaceRight,
		float[] echoLeft, float[] echoRight, float[] keyLeft, float[] keyRight, float[] gainSlots)
	{
		var patch = Patch;
		var macros = Instrument!.Macros;
		var rate = _rate;

		// The macros' effects this block
		var cutoffScale = 1f;
		var drive = patch.Drive;
		var gainScale = 1f;
		var sendSpace = patch.SendSpace;
		var sendEcho = patch.SendEcho;
		var vibratoScale = 1f;
		foreach (var macro in patch.Macros)
		{
			var value = macro.From + (macro.To - macro.From) * macros[macro.Macro.Id];
			switch (macro.Target)
			{
				case MacroTarget.FilterCutoff: cutoffScale *= value; break;
				case MacroTarget.FilterOctaves: cutoffScale *= MathF.Pow(2f, value); break;
				case MacroTarget.Drive: drive = value; break;
				case MacroTarget.Gain: gainScale *= value; break;
				case MacroTarget.SendSpace: sendSpace = value; break;
				case MacroTarget.SendEcho: sendEcho = value; break;
				case MacroTarget.VibratoDepth: vibratoScale *= value; break;
			}
		}

		// Pitch, once a block: the glide, the vibrato after its delay, the drop
		if (patch.GlideSeconds > 0f)
			CurrentNote += (TargetNote - CurrentNote) * (1f - MathF.Exp(-n / (patch.GlideSeconds * rate)));
		else
			CurrentNote = TargetNote;
		var age = AgeFrames / (float)rate;
		var vibrato = 0f;
		if (patch.VibratoSemitones != 0f && patch.VibratoHz > 0f)
		{
			_lfo.Set(patch.VibratoHz * n, rate);
			var lfo = _lfo.Next();
			if (age >= patch.VibratoDelaySeconds)
				vibrato = patch.VibratoSemitones * vibratoScale * lfo;
		}
		var drop = patch.PitchDropSemitones != 0f && patch.PitchDropSeconds > 0f
			? patch.PitchDropSemitones * MathF.Exp(-6.9078f * age / patch.PitchDropSeconds)
			: 0f;
		var note = CurrentNote + vibrato + drop;
		var hz = Units.NoteToHz(note);
		// The drop bends a fixed operator as well: a kick's sine at 50 Hz whatever the note still falls to it
		var dropRatio = drop == 0f ? 1f : MathF.Pow(2f, drop / 12f);

		// The operators, in order: each fills its own scratch block from those before it
		var specs = patch.Operators;
		for (var i = 0; i < specs.Length; i++)
		{
			var spec = specs[i];
			var output = scratch[i];
			ref var op = ref _ops[i];
			// An operator whose envelope has died is silence until the next note gates it: a 50 ms click on a long note costs
			// nothing after its 50 ms
			if (op.Env.IsIdle && spec.Kind is OperatorKind.Sine or OperatorKind.Wave or OperatorKind.Noise or OperatorKind.Level)
			{
				Array.Clear(output, 0, n);
				continue;
			}
			var level = spec.Level * (1f - spec.Velocity + spec.Velocity * Velocity) * op.KeyGain;
			foreach (var macro in patch.Macros)
				if (macro.Target == MacroTarget.OperatorLevel && macro.Operator == i)
					level *= macro.From + (macro.To - macro.From) * macros[macro.Macro.Id];
			var freq = spec.FixedHz > 0f ? spec.FixedHz * dropRatio : spec.DetuneCents == 0f ? hz * spec.Ratio : hz * spec.Ratio * Units.CentsToRatio(spec.DetuneCents);
			var dt = freq / rate;
			// The envelope a block at a time, times the level, for the operators that have one
			var env = _env.AsSpan(0, n);
			if (spec.Kind is not (OperatorKind.String or OperatorKind.Tube or OperatorKind.Modes))
			{
				op.Env.Fill(env);
				for (var s = 0; s < n; s++)
					env[s] *= level;
			}
			switch (spec.Kind)
			{
				case OperatorKind.Sine: RenderSine(ref op, spec, scratch, output, n, dt, _env); break;
				case OperatorKind.Wave: RenderWave(ref op, spec, output, n, dt, _env); break;
				case OperatorKind.Noise: RenderNoise(ref op, spec, output, n, _env); break;
				case OperatorKind.Level: env.CopyTo(output); break;
				case OperatorKind.Sample: RenderSample(ref op, spec, output, n, note, _env); break;
				case OperatorKind.String: RenderString(ref op, spec, scratch, output, n, hz, level, i); break;
				case OperatorKind.Tube: RenderTube(spec, scratch, output, n, hz, level); break;
				case OperatorKind.Modes: RenderModes(ref op, spec, scratch, output, n, hz, level, i); break;
			}
		}

		// The filter's cutoff for the block, its envelope stepped on by the block
		var filterSpec = patch.Filter;
		if (filterSpec is not null)
		{
			// Stepped only where it moves the cutoff: a filter with no envelope of its own has nothing to step
			var env = 0f;
			if (filterSpec.EnvelopeOctaves != 0f)
			{
				env = _filterEnvelope.Level;
				for (var s = 0; s < n; s++)
					_filterEnvelope.Next();
			}
			var octaves = filterSpec.EnvelopeOctaves * env + filterSpec.KeyTrack * (note - 60f) / 12f + filterSpec.Velocity * 4f * (Velocity - 1f);
			var cutoff = Math.Clamp(filterSpec.CutoffHz * MathF.Pow(2f, octaves) * cutoffScale, 20f, 0.45f * rate);
			_filter.Set(cutoff, filterSpec.Q, rate);
			_filterCutoff = cutoff;
		}

		var driveGain = 1f + 8f * Math.Clamp(drive, 0f, 1f);
		var driveNorm = 1f / MathF.Sqrt(driveGain);
		var gain = Velocity * Velocity * Units.DbToGain(patch.GainDb) * gainScale * Instrument.Gain;
		if (patch.TremoloDepth > 0f && patch.TremoloHz > 0f)
		{
			_tremolo.Set(patch.TremoloHz * n, rate);
			gain *= 1f - Math.Clamp(patch.TremoloDepth, 0f, 1f) * (0.5f + 0.5f * _tremolo.Next());
		}
		var pan = Math.Clamp(patch.Pan + NotePan, -1f, 1f);
		// A patch whose operators are heard apart is two channels from here on, each with the filter and the drive, and the
		// voice's pan leans the pair as a balance; any other is one, placed by its pan. For the one, the right is the left
		var wide = patch.Wide;
		float toLeft, toRight;
		if (wide)
		{
			toLeft = MathF.Min(1f, 1f - pan);
			toRight = MathF.Min(1f, 1f + pan);
		}
		else
		{
			var angle = (pan + 1f) * (MathF.PI / 4f);
			toLeft = MathF.Cos(angle);
			toRight = MathF.Sin(angle);
		}
		// The heard operators mixed, then filtered and driven, each a pass over the block
		var mix = _mix;
		var mixRight = wide ? _mixRight : _mix;
		var outputs = patch.Outputs;
		if (wide)
		{
			Array.Clear(mix, 0, n);
			Array.Clear(mixRight, 0, n);
			for (var t = 0; t < outputs.Length; t++)
			{
				var source = scratch[outputs[t].Operator];
				float l = _tapLeft[t], r = _tapRight[t];
				for (var s = 0; s < n; s++)
				{
					mix[s] += l * source[s];
					mixRight[s] += r * source[s];
				}
			}
		}
		else
		{
			var first = outputs[0];
			var source = scratch[first.Operator];
			for (var s = 0; s < n; s++)
				mix[s] = first.Level * source[s];
			for (var t = 1; t < outputs.Length; t++)
			{
				var tap = outputs[t];
				source = scratch[tap.Operator];
				for (var s = 0; s < n; s++)
					mix[s] += tap.Level * source[s];
			}
		}
		if (filterSpec is not null)
		{
			Filtered(mix, n, filterSpec.Kind, ref _filter);
			if (wide)
			{
				_filterRight.Set(_filterCutoff, filterSpec.Q, rate);
				Filtered(mixRight, n, filterSpec.Kind, ref _filterRight);
			}
		}
		if (drive > 0f)
		{
			for (var s = 0; s < n; s++)
				mix[s] = MathF.Tanh(mix[s] * driveGain) * driveNorm;
			if (wide)
				for (var s = 0; s < n; s++)
					mixRight[s] = MathF.Tanh(mixRight[s] * driveGain) * driveNorm;
		}
		var peak = 0f;
		for (var s = 0; s < n; s++)
			peak = MathF.Max(peak, MathF.Abs(mix[s]));
		if (wide)
			for (var s = 0; s < n; s++)
				peak = MathF.Max(peak, MathF.Abs(mixRight[s]));

		// Gain, the fade of a voice being stolen or choked, pan, and the sends
		// A gain slot the game moves while the note sounds (a part's level), ramped across the block from where it was
		var slot1 = GainSlot > 0 ? gainSlots[GainSlot - 1] : 1f;
		var slot0 = SlotGain < 0f ? slot1 : SlotGain;
		SlotGain = slot1;
		var slotStep = (slot1 - slot0) / n;
		var fade = Fade;
		var left = gain * toLeft;
		var right = gain * toRight;
		if (FadeStep > 0f || fade != 1f || slot0 != 1f || slot1 != 1f)
			for (var s = 0; s < n; s++)
			{
				if (FadeStep > 0f)
					fade = MathF.Max(0f, fade - FadeStep);
				var by = fade * (slot0 + slotStep * s);
				mix[s] *= by;
				if (wide)
					mixRight[s] *= by;
			}
		Fade = fade;
		var heard = 0f;
		for (var s = 0; s < n; s++)
			heard = MathF.Max(heard, MathF.Max(MathF.Abs(mix[s]), MathF.Abs(mixRight[s])));
		// A wide patch's two channels each hold what a centred one's single channel becomes only after its pan (1 / √2 of
		// it a side): so the two read alike on a meter when they sound alike
		LastPeak = MathF.Max(LastPeak, heard * gain * (wide ? 1.4142135f : 1f));
		var into = groupLeft.AsSpan(offset, n);
		var intoRight = groupRight.AsSpan(offset, n);
		for (var s = 0; s < n; s++)
		{
			into[s] += mix[s] * left;
			intoRight[s] += mixRight[s] * right;
		}
		// Most patches send nothing: skip the sends' buffers then
		if (sendSpace != 0f)
		{
			var l = left * sendSpace;
			var r = right * sendSpace;
			into = spaceLeft.AsSpan(offset, n);
			intoRight = spaceRight.AsSpan(offset, n);
			for (var s = 0; s < n; s++)
			{
				into[s] += mix[s] * l;
				intoRight[s] += mixRight[s] * r;
			}
		}
		if (sendEcho != 0f)
		{
			var l = left * sendEcho;
			var r = right * sendEcho;
			into = echoLeft.AsSpan(offset, n);
			intoRight = echoRight.AsSpan(offset, n);
			for (var s = 0; s < n; s++)
			{
				into[s] += mix[s] * l;
				intoRight[s] += mixRight[s] * r;
			}
		}
		if (patch.SendKey != 0f)
		{
			var l = left * patch.SendKey;
			var r = right * patch.SendKey;
			into = keyLeft.AsSpan(offset, n);
			intoRight = keyRight.AsSpan(offset, n);
			for (var s = 0; s < n; s++)
			{
				into[s] += mix[s] * l;
				intoRight[s] += mixRight[s] * r;
			}
		}
		_filter.Flush();
		_filterRight.Flush();
		AgeFrames += n;

		if (FadeStep > 0f && fade <= 0f)
			return true;
		return HeardIdle() && peak < Silent;
	}

	// A block through the voice's filter. The filter is worked on as a local copy, so its state stays in registers
	private static void Filtered(float[] block, int n, FilterKind kind, ref Svf state)
	{
		var filter = state;
		switch (kind)
		{
			case FilterKind.LowPass:
				for (var s = 0; s < n; s++)
					block[s] = filter.LowPass(block[s]);
				break;
			case FilterKind.BandPass:
				for (var s = 0; s < n; s++)
					block[s] = filter.BandPass(block[s]);
				break;
			default:
				for (var s = 0; s < n; s++)
					block[s] = filter.HighPass(block[s]);
				break;
		}
		filter.Flush();
		state = filter;
	}

	// Whether every operator the output hears has finished: its envelope idle (or a struck one died away), a sample past
	// its end, a string or tube with its drivers idle (a string with none, its burst spent)
	private bool HeardIdle()
	{
		var specs = Patch.Operators;
		for (var i = 0; i < specs.Length; i++)
		{
			var spec = specs[i];
			ref var op = ref _ops[i];
			_idle[i] = spec.Kind switch
			{
				OperatorKind.Sample => op.Data is null || op.Position >= op.Data.Samples.Length || (!spec.OneShot && EnvelopeDone(ref op, spec)),
				OperatorKind.String => spec.Drivers.Length == 0 ? op.Burst <= 0 : DriversIdle(spec),
				OperatorKind.Tube or OperatorKind.Modes => DriversIdle(spec),
				_ => EnvelopeDone(ref op, spec),
			};
		}
		foreach (var tap in Patch.Outputs)
			if (!_idle[tap.Operator])
				return false;
		return true;
	}

	private bool EnvelopeDone(ref OpState op, OperatorSpec spec) =>
		op.Env.IsIdle || (spec.Envelope.Sustain <= 0f && op.Env.Level < Silent && AgeFrames > _attackFrames + 1f);

	private bool DriversIdle(OperatorSpec spec)
	{
		foreach (var driver in spec.Drivers)
			if (!_idle[driver])
				return false;
		return true;
	}

	private static void RenderSine(ref OpState op, OperatorSpec spec, float[][] scratch, float[] output, int n, float dt, float[] env)
	{
		const float Cycles = 1f / TwoPi;   // radians of phase to cycles of the table
		var modulators = spec.Modulators;
		var phase = op.Phase;
		var o = output.AsSpan(0, n);
		var e = env.AsSpan(0, n);
		if (modulators.Length == 0 && spec.Feedback == 0f)
		{
			// A plain sine: the commonest carrier, and every additive partial
			for (var s = 0; s < o.Length; s++)
			{
				o[s] = SineTable.At(phase) * e[s];
				phase += dt;
				if (phase >= 1f)
					phase -= MathF.Floor(phase);
			}
			op.Phase = phase;
			op.Y1 = o[^1];
			op.Y2 = o.Length > 1 ? o[^2] : op.Y1;
			return;
		}
		if (modulators.Length == 1 && spec.Feedback == 0f)
		{
			// One modulator: the commonest pair
			var index = modulators[0].Index * Cycles;
			var m = scratch[modulators[0].Operator].AsSpan(0, n);
			for (var s = 0; s < o.Length; s++)
			{
				o[s] = SineTable.At(phase + index * m[s]) * e[s];
				phase += dt;
				if (phase >= 1f)
					phase -= MathF.Floor(phase);
			}
			op.Phase = phase;
			op.Y1 = o[^1];
			op.Y2 = o.Length > 1 ? o[^2] : op.Y1;
			return;
		}
		var feedback = spec.Feedback * 0.5f * Cycles;
		var y1 = op.Y1;
		var y2 = op.Y2;
		for (var s = 0; s < o.Length; s++)
		{
			var m = 0f;
			for (var j = 0; j < modulators.Length; j++)
				m += modulators[j].Index * scratch[modulators[j].Operator][s];
			var y = SineTable.At(phase + m * Cycles + feedback * (y1 + y2)) * e[s];
			y2 = y1;
			y1 = y;
			o[s] = y;
			phase += dt;
			if (phase >= 1f)
				phase -= MathF.Floor(phase);
		}
		op.Phase = phase;
		op.Y1 = y1;
		op.Y2 = y2;
	}

	private static void RenderWave(ref OpState op, OperatorSpec spec, float[] output, int n, float dt, float[] env)
	{
		var phase = op.Phase;
		var saw = spec.Waveform == Waveform.Saw;
		for (var s = 0; s < n; s++)
		{
			var y = saw ? PolyBlep.Saw(phase, dt) : PolyBlep.Square(phase, dt, spec.PulseWidth);
			output[s] = y * env[s];
			phase += dt;
			if (phase >= 1f)
				phase -= MathF.Floor(phase);
		}
		op.Phase = phase;
	}

	private static void RenderNoise(ref OpState op, OperatorSpec spec, float[] output, int n, float[] env)
	{
		for (var s = 0; s < n; s++)
		{
			var x = op.Noise.Next();
			if (spec.Pink)
				x = op.Pink.Tick(x);
			output[s] = x * env[s];
		}
	}

	private void RenderSample(ref OpState op, OperatorSpec spec, float[] output, int n, float note, float[] env)
	{
		var data = op.Data;
		if (data is null)
		{
			Array.Clear(output, 0, n);
			return;
		}
		var samples = data.Samples;
		var step = Units.NoteToHz(note) / op.RootHz * spec.Ratio * data.SampleRate / _rate;
		var position = op.Position;
		// A looped recording goes round its loop for as long as the voice sounds; a one-shot plays through to its end
		var looped = data.IsLooped && !spec.OneShot;
		var end = looped ? Math.Min(data.LoopEnd, samples.Length) : samples.Length;
		var loop = looped ? end - data.LoopStart : 0;
		for (var s = 0; s < n; s++)
		{
			if (position >= end)
			{
				if (!looped)
				{
					output[s] = 0f;
					continue;
				}
				do
					position -= loop;
				while (position >= end);
			}
			var i = (int)position;
			var t = (float)(position - i);
			var ym1 = At(samples, i - 1);
			var y0 = At(samples, i);
			// Past the loop's last frame, the curve's points are the loop's first: where the playing goes next
			var y1 = At(samples, looped && i + 1 >= end ? i + 1 - loop : i + 1);
			var y2 = At(samples, looped && i + 2 >= end ? i + 2 - loop : i + 2);
			var c1 = 0.5f * (y1 - ym1);
			var c2 = ym1 - 2.5f * y0 + 2f * y1 - 0.5f * y2;
			var c3 = 0.5f * (y2 - ym1) + 1.5f * (y0 - y1);
			output[s] = (((c3 * t + c2) * t + c1) * t + y0) * env[s];
			position += step;
		}
		op.Position = position;
	}

	private static float At(float[] samples, int i) => (uint)i < (uint)samples.Length ? samples[i] : 0f;

	private void RenderString(ref OpState op, OperatorSpec spec, float[][] scratch, float[] output, int n, float hz, float level, int index)
	{
		var line = _strings[_stringSlot[index]];
		var period = _rate / (hz * spec.Ratio);
		var smooth = 0.5f * Math.Clamp(spec.Damping, 0f, 1f);
		var delay = Math.Clamp(period - smooth, 2f, LineFrames - 6f);
		var loss = MathF.Pow(10f, -3f * period / (MathF.Max(spec.DecaySeconds * op.KeyTime, 0.01f) * _rate)) * op.LossScale;
		var drivers = spec.Drivers;
		var previous = op.Previous;
		for (var s = 0; s < n; s++)
		{
			var x = 0f;
			foreach (var driver in drivers)
				x += scratch[driver][s];
			if (drivers.Length == 0 && op.Burst > 0)
			{
				x = op.Noise.Next() * Velocity;
				op.Burst--;
			}
			var y = line.ReadHermite(delay);
			var f = (1f - smooth) * y + smooth * previous;
			previous = y;
			var written = x + loss * f;
			Denormal.Flush(ref written);
			line.Write(written);
			output[s] = y * level;
		}
		Denormal.Flush(ref previous);
		op.Previous = previous;
	}

	// A bank of resonances rung by the operator's drivers, each mode at its own frequency (a ratio of the note's, or
	// fixed) for its own time
	private void RenderModes(ref OpState op, OperatorSpec spec, float[][] scratch, float[] output, int n, float hz, float level, int index)
	{
		var struck = _env.AsSpan(0, n);
		struck.Clear();
		foreach (var driver in spec.Drivers)
		{
			var from = scratch[driver].AsSpan(0, n);
			for (var s = 0; s < n; s++)
				struck[s] += from[s];
		}
		var o = output.AsSpan(0, n);
		o.Clear();
		var modes = spec.Modes;
		var top = 0.45f * _rate;
		for (var m = 0; m < modes.Length; m++)
		{
			var mode = modes[m];
			var f = mode.Hz > 0f ? mode.Hz : hz * spec.Ratio * mode.Ratio;
			if (f >= top)
				continue;
			ref var ring = ref _modes[index * Mode.Max + m];
			ring.Set(f, mode.DecaySeconds * op.KeyTime, _rate);
			var gain = mode.Level * level;
			for (var s = 0; s < n; s++)
				o[s] += gain * ring.Tick(struck[s]);
			ring.Flush();
		}
	}

	// The flute of the Synthesis Toolkit (STK): a jet's delay driving a bore's, through the jet's cubic
	private void RenderTube(OperatorSpec spec, float[][] scratch, float[] output, int n, float hz, float level)
	{
		// The bore is a period and a half long, less what the loop's reflection low-pass and DC blocker delay the note's own
		// frequency (the blocker leads, more the lower the note), so it sounds in tune across the range
		var p = 0.7f - 0.1f * 22050f / _rate;
		var w = 2f * MathF.PI * hz * spec.Ratio / _rate;
		var lowPassDelay = MathF.Atan2(p * MathF.Sin(w), 1f - p * MathF.Cos(w)) / w;
		var blockerDelay = (MathF.Atan2(0.99f * MathF.Sin(w), 1f - 0.99f * MathF.Cos(w)) - 0.5f * (MathF.PI - w)) / w;
		var bore = Math.Clamp(1.5f * _rate / (hz * spec.Ratio) - lowPassDelay - blockerDelay, 2f, LineFrames - 6f);
		// Blown softer below G#4, as a flautist plays low notes, so they speak in their own register rather than a twelfth up
		var pressure = TubePressure * Math.Clamp(0.8f + 0.025f * (Units.HzToNote(hz * spec.Ratio) - 60f), 0.8f, 1f);
		var jet = Math.Clamp(bore * 0.32f, 2f, LineFrames - 6f);
		var drivers = spec.Drivers;
		for (var s = 0; s < n; s++)
		{
			var breath = 0f;
			foreach (var driver in drivers)
				breath += scratch[driver][s];
			breath *= pressure;
			_reflect = (1f - p) * _boreOut + p * _reflect;
			var t = -_reflect;
			_dc = t - _dcIn + 0.99f * _dc;
			_dcIn = t;
			var d = breath - 0.5f * _dc;
			var j = _jet.ReadHermite(jet);
			_jet.Write(d);
			j = Math.Clamp(j * (j * j - 1f), -1f, 1f) + 0.5f * _dc;
			_boreOut = _bore.ReadHermite(bore);
			_bore.Write(j);
			output[s] = 0.3f * _boreOut * level;
		}
		Denormal.Flush(ref _reflect);
		Denormal.Flush(ref _dc);
		Denormal.Flush(ref _dcIn);
		Denormal.Flush(ref _boreOut);
	}

	private struct OpState
	{
		public float Phase;
		public float Y1, Y2;
		public Envelope Env;
		public double Position;
		public Noise Noise;
		public PinkFilter Pink;
		public SampleData? Data;
		public float RootHz;
		public float Previous;
		public int Burst;
		public float LossScale;
		public float KeyGain;     // the operator's level for this note, from how it follows the keyboard
		public float KeyTime;     // and what its decay, release and ring are multiplied by
	}
}
