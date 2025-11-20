using SoundFlow.Abstracts.Devices;
using SoundFlow.Midi.PortMidi.Enums;
using SoundFlow.Structs;
using System.Runtime.InteropServices;
using SoundFlow.Midi.Devices;
using SoundFlow.Midi.PortMidi.Exceptions;
using SoundFlow.Midi.Structs;

namespace SoundFlow.Midi.PortMidi.Devices;

/// <summary>
/// A concrete implementation of a MIDI output device using the PortMidi backend.
/// </summary>
internal sealed class PortMidiOutputDevice : MidiOutputDevice
{
    private readonly nint _stream;

    /// <summary>
    /// Initializes a new instance of the <see cref="PortMidiOutputDevice"/> class.
    /// </summary>
    /// <param name="info">The device information.</param>
    internal PortMidiOutputDevice(MidiDeviceInfo info) : base(info)
    {
        var result = (PortMidiError)Native.Pm_OpenOutput(out _stream, info.Id, nint.Zero, 1024, nint.Zero, nint.Zero, 0);
        if (result != PortMidiError.NoError)
        {
            var error = MapError(result, $"open output device '{Info.Name}'");
            throw new PortBackendException(result, $"Failed to open PortMidi device. Reason: {error.Message}");
        }
    }

    /// <inheritdoc />
    public override Result SendMessage(MidiMessage message)
    {
        if (IsDisposed) return Result.Fail(new ObjectDisposedError(nameof(PortMidiOutputDevice)));
        
        var pmMessage = (message.Data2 << 16) | (message.Data1 << 8) | message.StatusByte;
        var result = (PortMidiError)Native.Pm_WriteShort(_stream, 0, pmMessage);
        return result == PortMidiError.NoError ? Result.Ok() : Result.Fail(MapError(result, $"send short message to '{Info.Name}'"));
    }

    /// <inheritdoc />
    public override Result SendSysEx(byte[] data)
    {
        if (IsDisposed) return Result.Fail(new ObjectDisposedError(nameof(PortMidiOutputDevice)));
        if (data.Length == 0) return Result.Fail(new ValidationError("SysEx data cannot be empty."));

        // PortMidi expects the full message including F0 and F7, but our API abstracts this away.
        var fullMessage = new byte[data.Length + 2];
        fullMessage[0] = 0xF0; // SysEx Start
        Buffer.BlockCopy(data, 0, fullMessage, 1, data.Length);
        fullMessage[^1] = 0xF7; // SysEx End

        var handle = GCHandle.Alloc(fullMessage, GCHandleType.Pinned);
        try
        {
            var result = (PortMidiError)Native.Pm_WriteSysEx(_stream, 0, handle.AddrOfPinnedObject());
            return result == PortMidiError.NoError ? Result.Ok() : Result.Fail(MapError(result, $"send SysEx message to '{Info.Name}'"));
        }
        finally
        {
            handle.Free();
        }
    }
    
    /// <inheritdoc />
    public override void Dispose()
    {
        if (IsDisposed) return;

        if (_stream != nint.Zero)
            Native.Pm_Close(_stream);

        IsDisposed = true;
    }

    /// <summary>
    /// Maps a <see cref="PortMidiError"/> code to an appropriate <see cref="IError"/> record.
    /// </summary>
    /// <param name="error">The error code returned from a PortMidi function.</param>
    /// <param name="operationDescription">A description of the operation that failed.</param>
    /// <returns>A descriptive error record.</returns>
    private IError MapError(PortMidiError error, string operationDescription)
    {
        return error switch
        {
            // Direct mapping to a specific error type
            PortMidiError.HostError => new HostError(Native.Pm_GetErrorText((int)error)),
            PortMidiError.InsufficientMemory => new OutOfMemoryError(),
            PortMidiError.DeviceIsBusy => new ResourceBusyError($"MIDI Device '{Info.Name}'"),

            // Errors indicating a problem with the device's state or connection
            PortMidiError.InvalidDeviceId or PortMidiError.BadPtr => 
                new DeviceStateError("The device is in an invalid state. It may have been disconnected or disposed incorrectly."),

            // Errors indicating an internal issue within the PortMidi library itself
            PortMidiError.BufferTooSmall or PortMidiError.BufferOverflow => 
                new InternalLibraryError("PortMidi", $"The internal device buffer is full during operation: {operationDescription}."),
            PortMidiError.InternalError => 
                new InternalLibraryError("PortMidi", $"An unspecified internal error occurred during operation: {operationDescription}."),
            PortMidiError.BadData =>
                new InternalLibraryError("PortMidi", "The library reported malformed data, which may indicate a bug."),

            // A fallback for any unhandled or unexpected error codes
            _ => new InternalLibraryError("PortMidi", $"An unexpected error occurred: {error} ({operationDescription}).")
        };
    }
}