namespace Loom.Audio.Synth.Chip;

/// <summary>
/// The recordings a <see cref="OperatorKind.Sample"/> operator picks from by note and velocity, with alternate takes of
/// the same range played in turn.
/// </summary>
public sealed class SampleSet
{
	private readonly SampleZone[] _zones;

	/// <summary>A set of <paramref name="zones"/>, in the order alternates are taken.</summary>
	/// <exception cref="ArgumentException">No zones, or a zone whose recording the chip can't play (no samples, no rate, a loop outside it).</exception>
	public SampleSet(params SampleZone[] zones)
	{
		ArgumentNullException.ThrowIfNull(zones);
		if (zones.Length == 0)
			throw new ArgumentException("A sample set has at least one zone.", nameof(zones));
		_zones = [.. zones];
		for (var z = 0; z < _zones.Length; z++)
		{
			ArgumentNullException.ThrowIfNull(_zones[z], nameof(zones));
			_zones[z].Data.Check($"Zone {z}");
		}
	}

	/// <summary>The zones, in the order given.</summary>
	public IReadOnlyList<SampleZone> Zones => _zones;

	/// <summary>
	/// The zone for <paramref name="note"/> at <paramref name="velocity"/>: among those whose ranges hold both (a velocity
	/// equal to a zone's <see cref="SampleZone.HighVelocity"/> belongs to it), the one at <paramref name="turn"/> mod their
	/// count, so a counter the caller steps per note alternates takes; with none, the one whose root is nearest. Allocates
	/// nothing, for the audio thread.
	/// </summary>
	public SampleZone Pick(int note, float velocity, int turn)
	{
		var count = 0;
		foreach (var zone in _zones)
			if (Holds(zone, note, velocity))
				count++;
		if (count > 0)
		{
			var wanted = (int)((uint)turn % (uint)count);
			foreach (var zone in _zones)
				if (Holds(zone, note, velocity) && wanted-- == 0)
					return zone;
		}
		var nearest = _zones[0];
		foreach (var zone in _zones)
			if (Math.Abs(zone.RootNote - note) < Math.Abs(nearest.RootNote - note))
				nearest = zone;
		return nearest;
	}

	private static bool Holds(SampleZone zone, int note, float velocity) =>
		note >= zone.LowNote && note <= zone.HighNote && velocity >= zone.LowVelocity && velocity <= zone.HighVelocity;
}
