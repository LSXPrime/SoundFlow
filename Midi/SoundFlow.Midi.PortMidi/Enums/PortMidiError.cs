namespace SoundFlow.Midi.PortMidi.Enums;

/// <summary>
/// Represents error codes returned by the PortMidi library.
/// </summary>
public enum PortMidiError
{
    /// <summary>
    /// No error occurred.
    /// </summary>
    NoError = 0,

    /// <summary>
    /// The host OS reported an error.
    /// </summary>
    HostError = -10000,

    /// <summary>
    /// An invalid device ID was provided.
    /// </summary>
    InvalidDeviceId,

    /// <summary>
    /// An insufficient amount of memory was available.
    /// </summary>
    InsufficientMemory,

    /// <summary>
    /// The stream buffer is full.
    /// </summary>
    BufferTooSmall,

    /// <summary>
    /// The stream buffer is empty.
    /// </summary>
    BufferOverflow,

    /// <summary>
    /// An invalid stream pointer was provided.
    /// </summary>
    BadPtr,

    /// <summary>
    /// An invalid buffer was provided.
    /// </summary>
    BadData,

    /// <summary>
    /// The operation is not supported or implemented.
    /// </summary>
    InternalError,

    /// <summary>
    /// The device is already in use.
    /// </summary>
    DeviceIsBusy
}