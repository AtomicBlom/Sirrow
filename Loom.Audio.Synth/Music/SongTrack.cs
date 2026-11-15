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
	internal Patch? Patch { get; }
	internal DrumKit? Kit { get; }
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
		OctaveValue = octave;
		return this;
	}

	/// <summary>Its level, in decibels.</summary>
	public SongTrack Level(float db)
	{
		GainDb = db;
		return this;
	}

	/// <summary>Where it sits, -1 left to 1 right.</summary>
	public SongTrack Pan(float pan)
	{
		PanValue = Math.Clamp(pan, -1f, 1f);
		return this;
	}

	/// <summary>
	/// A player's looseness: each note up to <paramref name="timingMs"/> late, never early, and its velocity up to
	/// <paramref name="velocity"/> (a share) either way, the same way every time the song plays.
	/// </summary>
	public SongTrack Humanise(float timingMs, float velocity)
	{
		HumaniseMs = Math.Max(0f, timingMs);
		HumaniseVelocity = Math.Max(0f, velocity);
		return this;
	}

	/// <summary>Swing: each off-beat step of <paramref name="stepTicks"/> pushed late, 0.5 straight, 0.667 a triplet feel.</summary>
	public SongTrack Swing(float ratio, int stepTicks = Ticks.Eighth)
	{
		SwingRatio = Math.Clamp(ratio, 0.5f, 0.9f);
		SwingStep = stepTicks;
		return this;
	}

	/// <summary>One of its patch's macros, to follow a cue or hold at a value.</summary>
	public TrackMacro Macro(Macro macro)
	{
		var existing = _macros.Find(m => m.Macro == macro);
		if (existing is not null)
			return existing;
		var made = new TrackMacro(macro);
		_macros.Add(made);
		return made;
	}

	/// <inheritdoc/>
	public override string ToString() => Name;
}
