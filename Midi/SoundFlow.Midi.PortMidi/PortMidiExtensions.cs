using SoundFlow.Abstracts;

namespace SoundFlow.Midi.PortMidi;

/// <summary>
/// Provides extension methods for integrating the PortMidi backend with a SoundFlow AudioEngine.
/// </summary>
public static class PortMidiExtensions
{
    /// <summary>
    /// Configures the <see cref="AudioEngine"/> to use the PortMidi backend for all MIDI operations.
    /// This enables MIDI device I/O, routing, and synchronization capabilities.
    /// </summary>
    /// <param name="engine">The audio engine instance to configure.</param>
    /// <returns>
    /// The created <see cref="PortMidiBackend"/> instance, which can be used to configure
    /// advanced features like MIDI synchronization.
    /// </returns>
    public static PortMidiBackend UsePortMidi(this AudioEngine engine)
    {
        var backend = new PortMidiBackend();
        engine.UseMidiBackend(backend);
        engine.UpdateMidiDevicesInfo();
        return backend;
    }
}