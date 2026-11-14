namespace Loom.Audio;

/// <summary>
/// A duck: one bus turned down while another has sound on it, made with
/// <see cref="AudioEngine.AddDuck(AudioBus, AudioBus, float, float, float, float, float)"/>.
/// </summary>
/// <param name="Index">The duck's slot in its engine.</param>
public readonly record struct AudioDuck(int Index);
