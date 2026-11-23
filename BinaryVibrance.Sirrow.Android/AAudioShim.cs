using System.Runtime.InteropServices;

namespace BinaryVibrance.Sirrow.Android;

/// <summary>
/// The AAudio callback shim, <c>libloomaaudio.so</c> (built from <c>Native/loom_aaudio.c</c>). Android builds
/// <c>libaaudio</c> with Control-Flow Integrity, which faults on a callback whose code lies in no loaded library, and the
/// entry point of a C# <see cref="UnmanagedCallersOnlyAttribute"/> method is a stub the .NET runtime allocated, in none.
/// So AAudio is handed the shim's functions, which do live in a library, and each forwards to the C# callbacks a
/// <see cref="Target"/> names: the stream's user data is the target, not the device.
/// </summary>
internal static unsafe class AAudioShim
{
	private static readonly Lazy<(nint Data, nint Error, string Failure)> Exports = new(Load);

	/// <summary>Why the shim can't be used, or "" when it can.</summary>
	public static string Failure => Exports.Value.Failure;

	/// <summary>The data callback to hand AAudio, with a <see cref="Target"/> as its user data.</summary>
	public static delegate* unmanaged[Cdecl]<nint, nint, void*, int, int> Data =>
		(delegate* unmanaged[Cdecl]<nint, nint, void*, int, int>)Exports.Value.Data;

	/// <summary>The error callback to hand AAudio, with a <see cref="Target"/> as its user data.</summary>
	public static delegate* unmanaged[Cdecl]<nint, nint, int, void> Error =>
		(delegate* unmanaged[Cdecl]<nint, nint, int, void>)Exports.Value.Error;

	private static (nint Data, nint Error, string Failure) Load()
	{
		if (!NativeLibrary.TryLoad("libloomaaudio.so", out var library))
			return (0, 0, "the AAudio callback shim, libloomaaudio.so, is not in the app");
		if (!NativeLibrary.TryGetExport(library, "loom_aaudio_data", out var data)
			|| !NativeLibrary.TryGetExport(library, "loom_aaudio_error", out var error))
			return (0, 0, "libloomaaudio.so exports no loom_aaudio_data and loom_aaudio_error, so it is not Loom's AAudio shim");
		return (data, error, "");
	}

	/// <summary>What the shim calls: <c>loom_aaudio_target</c> in <c>loom_aaudio.c</c>, field for field.</summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct Target
	{
		public delegate* unmanaged[Cdecl]<nint, nint, void*, int, int> Data;
		public delegate* unmanaged[Cdecl]<nint, nint, int, void> Error;
		public nint User;
	}
}
