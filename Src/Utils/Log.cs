namespace SoundFlow.Utils;

/// <summary>
/// Defines the severity levels for log messages.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Detailed information for debugging purposes.
    /// </summary>
    Debug,
    /// <summary>
    /// General informational messages about the library's operation.
    /// </summary>
    Info,
    /// <summary>
    /// Indicates a potential issue that does not prevent the current operation from completing.
    /// </summary>
    Warning,
    /// <summary>
    /// Indicates an error that has occurred, which may affect functionality.
    /// </summary>
    Error,
    /// <summary>
    /// Indicates a fatal error that has occurred, which will prevent the application from continuing.
    /// </summary>
    Critical
}

/// <summary>
/// Provides a centralized, decoupled logging mechanism for the SoundFlow library.
/// End-users can subscribe to the static OnLog event to capture and handle log messages.
/// </summary>
public static class Log
{
    /// <summary>
    /// Occurs when the SoundFlow library generates a log message.
    /// Subscribe to this event in your application to route logs to a console, file, or UI.
    /// </summary>
    public static event Action<LogLevel, string>? OnLog;

    /// <summary>
    /// For public library use. Invokes the OnLog event with a Debug level message.
    /// </summary>
    /// <param name="message">The log message.</param>
    public static void Debug(string message) => OnLog?.Invoke(LogLevel.Debug, message);

    /// <summary>
    /// For public library use. Invokes the OnLog event with an Info level message.
    /// </summary>
    /// <param name="message">The log message.</param>
    public static void Info(string message) => OnLog?.Invoke(LogLevel.Info, message);
    
    /// <summary>
    /// For public library use. Invokes the OnLog event with a Warning level message.
    /// </summary>
    /// <param name="message">The log message.</param>
    public static void Warning(string message) => OnLog?.Invoke(LogLevel.Warning, message);

    /// <summary>
    /// For public library use. Invokes the OnLog event with an Error level message.
    /// </summary>
    /// <param name="message">The log message.</param>
    public static void Error(string message) => OnLog?.Invoke(LogLevel.Error, message);
    
    /// <summary>
    /// For public library use. Invokes the OnLog event with a Critical level message.
    /// </summary>
    /// <param name="message">The log message.</param>
    public static void Critical(string message) => OnLog?.Invoke(LogLevel.Critical, message);
}