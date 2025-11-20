namespace SoundFlow.Exceptions;

/// <summary>
///     An exception thrown when an error occurs in a audio backend.
/// </summary>
/// <param name="backendName">The name of the audio backend that threw the exception.</param>
/// <param name="resultCode">The result returned by the audio backend.</param>
/// <param name="message">The error message of the exception.</param>
public class BackendException(string backendName, int resultCode, string message) : Exception(message)
{
    /// <summary>
    ///     The name of the audio backend that threw the exception.
    /// </summary>
    public string Backend { get; } = backendName;

    /// <summary>
    ///     The result returned by the audio backend.
    /// </summary>
    public int ResultCode { get; } = resultCode;

    /// <inheritdoc />
    public override string ToString() => $"Backend: {Backend}\nResult: {ResultCode}\nMessage: {Message}\nStackTrace: {StackTrace}";
}
