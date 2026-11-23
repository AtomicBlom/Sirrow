namespace BinaryVibrance.Sirrow.Effects;

/// <summary>The speaker cabinet a <see cref="Distortion"/> is heard through: filters shaped like one's response.</summary>
public enum Cabinet
{
	/// <summary>No cabinet: the distortion as it is.</summary>
	None,
	/// <summary>One 12-inch speaker: lean lows, a forward upper middle, highs rolled off early.</summary>
	OneByTwelve,
	/// <summary>Four 12-inch speakers: a thump in the lows, a bite in the middle, darker highs.</summary>
	FourByTwelve,
}
