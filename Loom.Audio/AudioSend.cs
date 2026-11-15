namespace Loom.Audio;

/// <summary>
/// A send: a share of one bus's sound mixed into another as well as into its parent, made with
/// <see cref="AudioEngine.AddSend(AudioBus, AudioBus, float)"/>. The usual use is a reverb bus several buses send to.
/// </summary>
/// <param name="Index">The send's slot in its engine.</param>
public readonly record struct AudioSend(int Index);
