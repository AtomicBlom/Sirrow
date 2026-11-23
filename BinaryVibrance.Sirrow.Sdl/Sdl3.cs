using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BinaryVibrance.Sirrow.Sdl;

/// <summary>
/// The few SDL 3 functions the audio device plays through (<see cref="SdlAudioDevice"/>): our own binding. Every
/// signature is blittable (pointers, integers, SDL's one-byte <c>bool</c> as <see cref="byte"/>, handles as
/// <see cref="nint"/>), so <see cref="LibraryImportAttribute"/>'s generated stubs marshal nothing. Declarations follow SDL
/// 3.4's headers.
/// </summary>
/// <remarks>
/// SDL3.dll comes from the <c>BinaryVibrance.Kaldra.SDL3</c> package, which the app's deps.json lists, so the runtime finds
/// it as it finds any package's native library. Other code in the process may use the same SDL (a desktop host reading
/// gamepads): SDL counts each subsystem's starts, and the device starts and stops only its own.
/// </remarks>
internal static unsafe partial class Sdl3
{
	private const string Lib = "SDL3";

	public const uint InitAudio = 0x00000010;                  // SDL_INIT_AUDIO: implies SDL_INIT_EVENTS
	public const uint AudioF32 = 0x8120;                       // SDL_AUDIO_F32LE: 32-bit float, little-endian
	public const uint AudioDeviceDefaultPlayback = 0xFFFFFFFF; // SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void SDL_Quit();

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial byte SDL_SetHint(byte* name, byte* value);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial byte* SDL_GetError();

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial byte SDL_InitSubSystem(uint flags);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void SDL_QuitSubSystem(uint flags);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial uint SDL_WasInit(uint flags);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial byte SDL_GetAudioDeviceFormat(uint device, SdlAudioSpec* spec, int* sampleFrames);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial nint SDL_OpenAudioDeviceStream(uint device, SdlAudioSpec* spec, delegate* unmanaged[Cdecl]<nint, nint, int, int, void> callback, nint userdata);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial uint SDL_GetAudioStreamDevice(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial byte* SDL_GetAudioDeviceName(uint device);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial byte SDL_PauseAudioStreamDevice(nint stream);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial byte SDL_ResumeAudioStreamDevice(nint stream);

	// Not SuppressGCTransition: it takes the stream's lock, which the device's thread may hold
	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial byte SDL_PutAudioStreamData(nint stream, void* buffer, int length);

	[LibraryImport(Lib), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
	public static partial void SDL_DestroyAudioStream(nint stream);

	/// <summary>Whether SDL3.dll is there and loads, with the reason when it isn't.</summary>
	public static bool TryLoad(out string reason)
	{
		if (!NativeLibrary.TryLoad(Lib, typeof(Sdl3).Assembly, null, out var handle))
		{
			reason = OperatingSystem.IsWindows()
				? "SDL3.dll is not beside the app (the BinaryVibrance.Kaldra.SDL3 package carries it)"
				: "SDL 3 is not installed (the BinaryVibrance.Kaldra.SDL3 package carries Windows builds only)";
			return false;
		}
		NativeLibrary.Free(handle);
		reason = "";
		return true;
	}

	/// <summary>
	/// Stops the subsystems in <paramref name="flags"/>, and SDL itself once nothing else uses it. SDL counts each
	/// subsystem's starts, so this stops only what the device started.
	/// </summary>
	public static void QuitSubSystem(uint flags)
	{
		SDL_QuitSubSystem(flags);
		if (SDL_WasInit(0) == 0)
			SDL_Quit();
	}

	/// <summary>A NUL-terminated UTF-8 string of SDL's, as a managed string ("" for null).</summary>
	public static string Text(byte* p) => p == null ? "" : Marshal.PtrToStringUTF8((nint)p) ?? "";

	/// <summary>Sets one of SDL's hints (before the subsystem starts, for the ones read then).</summary>
	public static void Hint(string name, string value)
	{
		Span<byte> n = stackalloc byte[name.Length * 3 + 1];
		Span<byte> v = stackalloc byte[value.Length * 3 + 1];
		n[System.Text.Encoding.UTF8.GetBytes(name, n)] = 0;
		v[System.Text.Encoding.UTF8.GetBytes(value, v)] = 0;
		fixed (byte* pn = n, pv = v)
			SDL_SetHint(pn, pv);
	}
}
