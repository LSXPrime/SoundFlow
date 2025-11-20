using SoundFlow.Abstracts;
using SoundFlow.Midi.Devices;
using SoundFlow.Midi.Structs;

namespace SoundFlow.Interfaces;

/// <summary>
/// Defines the contract for a pluggable MIDI backend, responsible for creating and managing MIDI devices.
/// </summary>
public interface IMidiBackend : IDisposable
{
    /// <summary>
    /// Initializes the backend and provides it with a reference to the parent audio engine.
    /// This allows the backend to subscribe to engine lifecycle events for features like synchronization.
    /// </summary>
    /// <param name="engine">The parent audio engine instance.</param>
    void Initialize(AudioEngine engine);
    
    /// <summary>
    /// Creates a MIDI input device instance for the specified device.
    /// </summary>
    /// <param name="deviceInfo">The informational struct for the device to create.</param>
    /// <returns>An initialized <see cref="MidiInputDevice"/>.</returns>
    MidiInputDevice CreateMidiInputDevice(MidiDeviceInfo deviceInfo);

    /// <summary>
    /// Creates a MIDI output device instance for the specified device.
    /// </summary>
    /// <param name="deviceInfo">The informational struct for the device to create.</param>
    /// <returns>An initialized <see cref="MidiOutputDevice"/>.</returns>
    MidiOutputDevice CreateMidiOutputDevice(MidiDeviceInfo deviceInfo);
    
    /// <summary>
    /// Retrieves the list of available MIDI input and output devices from the backend.
    /// </summary>
    /// <param name="inputs">An output parameter that will be populated with the list of available MIDI input devices.</param>
    /// <param name="outputs">An output parameter that will be populated with the list of available MIDI output devices.</param>
    void UpdateMidiDevicesInfo(out MidiDeviceInfo[] inputs, out MidiDeviceInfo[] outputs);
}