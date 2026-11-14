namespace Loom.Audio.Mixing;

/// <summary>One command from the game thread to the mixer, copied through an <see cref="AudioEventQueue{T}"/>.</summary>
internal struct AudioCommand
{
	public AudioCommandKind Kind;
	public bool Loop;
	public int Index;
	public int Bus;
	public int Frames;
	public float A;
	public float B;
	public float C;
	public object? Payload;
}
