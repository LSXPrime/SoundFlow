using SoundFlow.Abstracts;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Midi.Abstracts;

/// <summary>
/// Represents a base class for an initialized MIDI device, managed by an <see cref="AudioEngine"/>.
/// </summary>
public abstract class MidiDevice : IDisposable
{
    /// <summary>
    /// Gets the informational struct for the physical MIDI device.
    /// </summary>
    public MidiDeviceInfo Info { get; protected init; }

    /// <summary>
    /// Gets a value indicating whether this device has been disposed.
    /// </summary>
    public bool IsDisposed { get; protected set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiDevice"/> class.
    /// </summary>
    /// <param name="info">The device information.</param>
    protected MidiDevice(MidiDeviceInfo info)
    {
        Info = info;
    }

    /// <summary>
    /// Releases all resources used by the MIDI device.
    /// </summary>
    public abstract void Dispose();
}