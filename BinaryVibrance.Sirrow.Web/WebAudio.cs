using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace BinaryVibrance.Sirrow.Web;

/// <summary>The browser's audio output, through an AudioWorklet: what an app's browser entry point registers.</summary>
[SupportedOSPlatform("browser")]
public static class WebAudio
{
	// Served as this package's static web asset; relative to _framework/, where the runtime resolves a module's URL from
	private const string ModuleUrl = "../_content/BinaryVibrance.Sirrow.Web/sirrow-audio.js";

	private static Task? _loaded;

	/// <summary>
	/// Loads the output's page module (<c>sirrow-audio.js</c>, which loads its worklet) and adds the output to
	/// <see cref="AudioOutput"/>. Await it in the app's Main before anything opens audio. Calling it again does nothing more.
	/// </summary>
	public static Task RegisterAsync()
	{
		return _loaded ??= Load();

		static async Task Load()
		{
			await JSHost.ImportAsync(WebAudioDevice.Module, ModuleUrl);
			AudioOutput.Register("WebAudio", options => (new WebAudioDevice(options), ""));
		}
	}
}
