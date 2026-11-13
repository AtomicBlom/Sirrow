using System.Runtime.InteropServices;

namespace Loom.Audio;

/// <summary>
/// An index alone on its cache line, so two threads each writing their own (a queue's head and tail) don't slow each
/// other down by sharing one. Not nested in <see cref="AudioEventQueue{T}"/>: a type nested in a generic type is generic
/// itself, and the runtime refuses explicit layout on a generic type.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedIndex
{
	[FieldOffset(64)] public int Value;
}
