using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Loom.Hosting.Android.Audio;

/// <summary>
/// The few AAudio functions the audio device uses (<c>libaaudio.so</c>, the NDK's C audio API, on every device from
/// Android 8). Our own binding: every signature is blittable — handles as <see cref="nint"/>, enums as <see cref="int"/>,
/// callbacks as unmanaged function pointers — so the generated stubs marshal nothing. Values follow the NDK's
/// <c>aaudio/AAudio.h</c>.
/// </summary>
internal static unsafe partial class AAudio
{
	private const string Lib = "aaudio";

	public const int Ok = 0;
	public const int ErrorDisconnected = -899;        // AAUDIO_ERROR_DISCONNECTED: the output went (headphones unplugged)
	public const int DirectionOutput = 0;
	public const int FormatPcmFloat = 2;
	public const int SharingModeExclusive = 0;
	public const int SharingModeShared = 1;
	public const int PerformanceModeLowLatency = 12;
	public const int UsageGame = 14;
	public const int CallbackResultContinue = 0;

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudio_createStreamBuilder(nint* builder);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void AAudioStreamBuilder_setDirection(nint builder, int direction);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void AAudioStreamBuilder_setFormat(nint builder, int format);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void AAudioStreamBuilder_setChannelCount(nint builder, int channelCount);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void AAudioStreamBuilder_setSampleRate(nint builder, int sampleRate);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void AAudioStreamBuilder_setSharingMode(nint builder, int sharingMode);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void AAudioStreamBuilder_setPerformanceMode(nint builder, int mode);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void AAudioStreamBuilder_setUsage(nint builder, int usage);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void AAudioStreamBuilder_setDataCallback(nint builder, delegate* unmanaged[Cdecl]<nint, nint, void*, int, int> callback, nint userData);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void AAudioStreamBuilder_setErrorCallback(nint builder, delegate* unmanaged[Cdecl]<nint, nint, int, void> callback, nint userData);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStreamBuilder_openStream(nint builder, nint* stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStreamBuilder_delete(nint builder);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_requestStart(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_requestPause(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_requestStop(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_close(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_getSampleRate(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_getChannelCount(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_getFormat(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_getFramesPerBurst(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_getBufferCapacityInFrames(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_setBufferSizeInFrames(nint stream, int frames);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_getSharingMode(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial int AAudioStream_getPerformanceMode(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial byte* AAudio_convertResultToText(int result);

	/// <summary>An AAudio result as its name, as AAUDIO_ERROR_DISCONNECTED.</summary>
	public static string Text(int result) => Marshal.PtrToStringUTF8((nint)AAudio_convertResultToText(result)) ?? result.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
