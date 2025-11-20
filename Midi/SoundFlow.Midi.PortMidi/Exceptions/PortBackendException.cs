using SoundFlow.Exceptions;
using SoundFlow.Midi.PortMidi.Enums;

namespace SoundFlow.Midi.PortMidi.Exceptions;

/// <summary>
/// An exception thrown when an error occurs in the PortMidi backend.
/// </summary>
/// <param name="errorCode">The error code returned by the PortMidi library.</param>
/// <param name="message">The error message of the exception.</param>
public class PortBackendException(PortMidiError errorCode, string? message = null) : BackendException("PortMidi", (int)errorCode, message ?? GetErrorMessage(errorCode))
{
    /// <summary>
    /// Gets the PortMidi error code that caused the exception.
    /// </summary>
    public PortMidiError ErrorCode { get; } = errorCode;

    private static string GetErrorMessage(PortMidiError errorCode)
    {
        var errorText = Native.Pm_GetErrorText((int)errorCode);
        return $"PortMidi error ({errorCode}): {errorText}";
    }

    /// <inheritdoc />
    public override string ToString() => $"ErrorCode: {ErrorCode}\nMessage: {Message}\nStackTrace: {StackTrace}";
}