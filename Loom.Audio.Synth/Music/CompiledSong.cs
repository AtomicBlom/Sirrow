using Loom.Audio.Synth.Chip;
using Loom.Audio.Synth.Dsp;

namespace Loom.Audio.Synth.Music;

/// <summary>
/// A song made into what the sequencer reads on the audio thread: arrays of structs, indexed by small integers, never
/// changed once made. Made on the game thread in one scale.
/// </summary>
internal sealed class CompiledSong
{
	private CompiledSong(Song song, Scale scale)
	{
		Song = song;
		Scale = scale;
	}

	public Song Song { get; }
	public Scale Scale { get; }
	public float Tempo { get; private init; }
	public int TicksPerBar { get; private init; }
	public int TicksPerBeat { get; private init; }
	public CompiledTrack[] Tracks { get; private init; } = [];
	public CueBase[] Cues { get; private init; } = [];
	public CompiledSection[] Sections { get; private init; } = [];
	public int[] Order { get; private init; } = [];       // section indices in turn; empty when conducted
	public bool Conducted { get; private init; }
	public int LatchCount { get; private init; }
	public int MostPlacements { get; private init; }
	public ChipOptions ChipOptions { get; private init; } = new();

	public int SlotOf(CueBase cue) => Array.IndexOf(Cues, cue);

	// The most values a condition's postfix steps hold at once: the sequencer's stack, made once
	public const int ConditionDepth = 16;

	// How many values a program's steps hold at once, at their most
	private static int Depth(List<ConditionOp> program)
	{
		int top = 0, most = 0;
		foreach (var op in program)
		{
			top += op.Kind switch
			{
				ConditionOpKind.And or ConditionOpKind.Or => -1,
				ConditionOpKind.Not => 0,
				_ => 1,
			};
			most = Math.Max(most, top);
		}
		return most;
	}

	public static CompiledSong Compile(Song song, Scale scale)
	{
		ArgumentNullException.ThrowIfNull(song);
		ArgumentNullException.ThrowIfNull(scale);
		if (song.Sections.Count == 0)
			throw new InvalidOperationException($"Song '{song.Name}' has no sections.");
		if (!float.IsFinite(song.Tempo) || song.Tempo <= 0f)
			throw new InvalidOperationException($"Song '{song.Name}' has a tempo of {song.Tempo}: a tempo is more than 0 beats a minute.");
		if (song.Voices <= 0)
			throw new InvalidOperationException($"Song '{song.Name}' has {song.Voices} voices: a song needs at least one.");
		if (song.Meter.Beats <= 0 || song.Meter.Unit <= 0)
			throw new InvalidOperationException($"Song '{song.Name}' has a meter of {song.Meter}: both its numbers are more than 0.");
		var ticksPerBar = song.Meter.TicksPerBar;
		var cues = song.Cues.ToArray();
		int Slot(CueBase cue) => Array.IndexOf(cues, cue);

		var tracks = new CompiledTrack[song.Tracks.Count];
		for (var t = 0; t < tracks.Length; t++)
		{
			var track = song.Tracks[t];
			var group = track.Group?.Index ?? 0;
			tracks[t] = new CompiledTrack
			{
				Name = track.Name,
				Patch = track.Patch,
				Kit = track.Kit,
				Group = group,
				Gain = Units.DbToGain(track.GainDb),
				Pan = track.PanValue,
				HumaniseMs = track.HumaniseMs,
				HumaniseVelocity = track.HumaniseVelocity,
				Seed = Seed(track.Name),
				// A macro some section sweeps and the track says nothing about is held at 0 (where every macro starts), so
				// it goes back there when the section that swept it is over
				Macros =
				[
					.. track.Macros.Select(m => new MacroFollow(m.Macro.Id, m.Cue is null ? -1 : Slot(m.Cue), m.From, m.To)),
					.. song.Sections.SelectMany(s => s.Sweeps)
						.Where(w => w.Track == track && track.Macros.All(m => m.Macro != w.Macro))
						.Select(w => w.Macro.Id).Distinct().Select(id => new MacroFollow(id, -1, 0f, 0f)),
				],
			};
		}

		var latches = 0;
		var gainSlots = 0;
		var sections = new CompiledSection[song.Sections.Count];
		var most = 0;
		for (var s = 0; s < sections.Length; s++)
		{
			var section = song.Sections[s];
			var length = section.Bars * ticksPerBar;
			var placements = new CompiledPlacement[section.Placements.Count];
			for (var p = 0; p < placements.Length; p++)
			{
				var placement = section.Placements[p];
				var track = placement.Track;
				var context = new PartContext(scale, section.Chords, ticksPerBar, track.OctaveValue, track.Kit, song.Tempo, track.SwingRatio, track.SwingStep);
				var events = new List<SongEvent>();
				var parsed = placement.Parsed;
				// The part repeated through the section, cut at its end (or at the last bar, which a fill takes)
				var limit = placement.Fill is null ? length : length - ticksPerBar;
				for (var offset = 0; offset < limit; offset += parsed.LengthTicks)
					parsed.Emit(context, offset, limit, events);
				placement.Fill?.Emit(context, length - ticksPerBar, length, events);

				events.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Note.CompareTo(b.Note));

				var program = new List<ConditionOp>();
				if (placement.Condition is { } condition)
				{
					Emit(condition, program, Slot, ref latches);
					if (Depth(program) > ConditionDepth)
						throw new InvalidOperationException(
							$"Song '{song.Name}': the condition on '{placement.Name}' nests deeper than {ConditionDepth}: write it flatter.");
				}
				var levelSlot = placement.LevelCue is { } level ? Slot(level) : -1;
				var gainSlot = -1;
				if (levelSlot >= 0)
				{
					if (gainSlots == ChipCore.GainSlotCount)
						throw new InvalidOperationException($"Song '{song.Name}' has more than {ChipCore.GainSlotCount} parts whose level follows a cue.");
					gainSlot = gainSlots++;
				}
				placements[p] = new CompiledPlacement
				{
					Source = placement,
					Name = placement.Name,
					Track = track.Index,
					Events = [.. events],
					LoopTicks = (parsed.LengthTicks + ticksPerBar - 1) / ticksPerBar * ticksPerBar,
					Condition = [.. program],
					LevelSlot = levelSlot,
					GainSlot = gainSlot,
					OnPhrase = placement.OnPhraseValue,
				};
			}
			most = Math.Max(most, placements.Length);
			sections[s] = new CompiledSection
			{
				Source = section,
				Name = section.Name,
				LengthTicks = length,
				Placements = placements,
				Sweeps = [.. section.Sweeps.Select(w => (w.Track.Index, w.Macro.Id, w.From, w.To))],
			};
		}

		var order = song.Conductor is not null ? [] : song.OrderList.Count > 0 ? song.OrderList.Select(o => o.Index).ToArray() : [0];
		ChipGroup[] groups = [new("main"), .. song.Groups.Select(g => new ChipGroup(g.Name, g.Effects))];
		return new CompiledSong(song, scale)
		{
			Tempo = song.Tempo,
			TicksPerBar = ticksPerBar,
			TicksPerBeat = ticksPerBar / song.Meter.Beats,
			Tracks = tracks,
			Cues = cues,
			Sections = sections,
			Order = order,
			Conducted = song.Conductor is not null,
			LatchCount = latches,
			MostPlacements = most,
			ChipOptions = new ChipOptions
			{
				Voices = song.Voices,
				Groups = groups,
				SpaceEffects = song.SpaceEffects,
				EchoEffects = song.EchoEffects,
				MasterEffects = song.MasterEffects,
				MaxInstruments = Song.MaxTracks,
				EventCapacity = 64,
			},
		};
	}

	// A condition as postfix steps; Above and Below each get a latch for the 0.05 margin
	private static void Emit(Condition condition, List<ConditionOp> program, Func<CueBase, int> slot, ref int latches)
	{
		switch (condition)
		{
			case AboveCondition above:
				program.Add(new ConditionOp { Kind = ConditionOpKind.Above, Slot = slot(above.Cue), Value = above.Level, Latch = latches++ });
				break;
			case BelowCondition below:
				program.Add(new ConditionOp { Kind = ConditionOpKind.Below, Slot = slot(below.Cue), Value = below.Level, Latch = latches++ });
				break;
			case IsCondition @is:
				program.Add(new ConditionOp { Kind = ConditionOpKind.Is, Slot = slot(@is.Cue), Value = @is.Value });
				break;
			case AndCondition and:
				Emit(and.A, program, slot, ref latches);
				Emit(and.B, program, slot, ref latches);
				program.Add(new ConditionOp { Kind = ConditionOpKind.And });
				break;
			case OrCondition or:
				Emit(or.A, program, slot, ref latches);
				Emit(or.B, program, slot, ref latches);
				program.Add(new ConditionOp { Kind = ConditionOpKind.Or });
				break;
			case NotCondition not:
				Emit(not.A, program, slot, ref latches);
				program.Add(new ConditionOp { Kind = ConditionOpKind.Not });
				break;
		}
		if (program.Count > 64)
			throw new InvalidOperationException("A condition has more than 64 steps.");
	}

	// A stable hash of a track's name (FNV-1a), so its humanising is the same every time
	private static uint Seed(string name)
	{
		var h = 2166136261u;
		foreach (var c in name)
			h = (h ^ c) * 16777619u;
		return h;
	}
}
