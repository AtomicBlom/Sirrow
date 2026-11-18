using Loom.Audio.Synth.Chip;

namespace Loom.Audio.Synth.Music;

/// <summary>One instrument of a song: a patch or a drum kit, with its octave, level, pan, feel and macros. Its setters chain.</summary>
public sealed class SongTrack
{
	private readonly List<TrackMacro> _macros = [];

	internal SongTrack(Song song, string name, int index, Patch? patch, DrumKit? kit, SongGroup? group)
	{
		Song = song;
		Name = name;
		Index = index;
		Patch = patch;
		Kit = kit;
		Group = group;
	}

	/// <summary>Its name.</summary>
	public string Name { get; }

	internal Song Song { get; }
	internal int Index { get; }
	/// <summary>The patch its notes play on, or null for a kit's track.</summary>
	public Patch? Patch { get; }
	/// <summary>The kit its hits play on, or null for a patch's track.</summary>
	public DrumKit? Kit { get; }
	internal SongGroup? Group { get; }
	internal int OctaveValue { get; private set; } = 4;
	internal float GainDb { get; private set; }
	internal float PanValue { get; private set; }
	internal float HumaniseMs { get; private set; }
	internal float HumaniseVelocity { get; private set; }
	internal float SwingRatio { get; private set; } = 0.5f;
	internal int SwingStep { get; private set; } = Ticks.Eighth;
	internal IReadOnlyList<TrackMacro> Macros => _macros;

	/// <summary>The octave its lines' degree 1 sounds in: 4 (middle C's) unless set.</summary>
	public SongTrack Octave(int octave)
	{
		Song.CheckOpen();

		OctaveValue = octave;
		return this;
	}

	/// <summary>Its level, in decibels.</summary>
	public SongTrack Level(float db)
	{
		Song.CheckOpen();

		GainDb = db;
		return this;
	}

	/// <summary>Where it sits, -1 left to 1 right.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A pan outside -1 to 1.</exception>
	public SongTrack Pan(float pan)
	{
		Song.CheckOpen();

		if (!float.IsFinite(pan) || pan < -1f || pan > 1f)
			throw new ArgumentOutOfRangeException(nameof(pan), pan, $"Track '{Name}': a pan runs from -1 (left) to 1 (right).");
		PanValue = pan;
		return this;
	}

	/// <summary>
	/// A player's looseness: each note late by up to twice <paramref name="timingMs"/> (by <paramref name="timingMs"/> on
	/// average), never early, and its velocity up to <paramref name="velocity"/> (a share) either way, the same way every
	/// time the song plays.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">A time or a share below 0.</exception>
	public SongTrack Humanise(float timingMs, float velocity)
	{
		Song.CheckOpen();

		if (!float.IsFinite(timingMs) || timingMs < 0f)
			throw new ArgumentOutOfRangeException(nameof(timingMs), timingMs, $"Track '{Name}': a looseness in time is 0 or more milliseconds.");
		if (!float.IsFinite(velocity) || velocity < 0f || velocity > 1f)
			throw new ArgumentOutOfRangeException(nameof(velocity), velocity, $"Track '{Name}': a looseness in velocity is a share, 0 to 1.");
		HumaniseMs = timingMs;
		HumaniseVelocity = velocity;
		return this;
	}

	/// <summary>
	/// Swing: each off-beat step of <paramref name="stepTicks"/> pushed late, 0.5 straight, 0.667 a triplet feel. The grid
	/// is swung before anything is placed off it, so a strum's strings move together.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">A ratio outside 0.5 to 0.9, or a step of no ticks.</exception>
	public SongTrack Swing(float ratio, int stepTicks = Ticks.Eighth)
	{
		Song.CheckOpen();

		if (!float.IsFinite(ratio) || ratio < 0.5f || ratio > 0.9f)
			throw new ArgumentOutOfRangeException(nameof(ratio), ratio, $"Track '{Name}': swing runs from 0.5 (straight) to 0.9.");
		if (stepTicks <= 0)
			throw new ArgumentOutOfRangeException(nameof(stepTicks), stepTicks, $"Track '{Name}': swing's step is more than 0 ticks.");
		SwingRatio = ratio;
		SwingStep = stepTicks;
		return this;
	}

	/// <summary>One of its patch's macros, to follow a cue or hold at a value.</summary>
	public TrackMacro Macro(Macro macro)
	{
		Song.CheckOpen();
		var existing = _macros.Find(m => m.Macro == macro);
		if (existing is not null)
			return existing;
		var made = new TrackMacro(Song, macro);
		_macros.Add(made);
		return made;
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
