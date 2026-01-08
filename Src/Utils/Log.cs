using System.Runtime.CompilerServices;

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
/// Represents a single log event.
/// Using a 'readonly struct' avoids heap allocation and GC pressure for high-performance scenarios.
/// </summary>
public readonly struct LogEntry
{
    /// <summary>
    /// Gets the severity level of the log entry.
    /// </summary>
    public LogLevel Level { get; }
    
    /// <summary>
    /// Gets the log message.
    /// </summary>
    public string Message { get; }
    
    /// <summary>
    /// Gets the date and time when the log entry was created.
    /// </summary>
    public DateTime Timestamp { get; }
    
    /// <summary>
    /// Gets information about the calling member (e.g., 'ClassName.MethodName').
    /// </summary>
    public string Caller { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LogEntry"/> struct.
    /// </summary>
    /// <param name="level">The severity level of the log entry.</param>
    /// <param name="message">The log message.</param>
    /// <param name="timestamp">The date and time of the log entry.</param>
    /// <param name="caller">Information about the calling member.</param>
    public LogEntry(LogLevel level, string message, DateTime timestamp, string caller)
    {
        Level = level;
        Message = message;
        Timestamp = timestamp;
        Caller = caller;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[{Timestamp:HH:mm:ss}] {Level.ToString().ToUpper()} {Caller}: {Message}";
    }
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
    public static event Action<LogEntry>? OnLog;

    /// <summary>
    /// Creates a <see cref="LogEntry"/> and invokes the <see cref="OnLog"/> event.
    /// </summary>
    /// <param name="level">The severity level for the log entry.</param>
    /// <param name="message">The log message.</param>
    /// <param name="memberName">The name of the calling member. Populated by the compiler.</param>
    /// <param name="filePath">The path of the source file of the caller. Populated by the compiler.</param>
    private static void Dispatch(LogLevel level, string message, string memberName, string filePath)
    {
        // Early exit if no one is listening.
        if (OnLog == null) return;

        var className = Path.GetFileNameWithoutExtension(filePath);
        var fullCaller = $"{className}.{memberName}";

        // This now creates a struct, which is extremely cheap.
        var entry = new LogEntry(level, message, DateTime.Now, fullCaller);

        OnLog.Invoke(entry);
    }

    /// <summary>
    /// Logs a message with the <see cref="LogLevel.Debug"/> severity level.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="member">The name of the calling member. This is automatically populated by the compiler and should not be set manually.</param>
    /// <param name="path">The path of the source file of the caller. This is automatically populated by the compiler and should not be set manually.</param>
    public static void Debug(string message,
        [CallerMemberName] string member = "", [CallerFilePath] string path = "")
        => Dispatch(LogLevel.Debug, message, member, path);

    /// <summary>
    /// Logs a message with the <see cref="LogLevel.Info"/> severity level.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="member">The name of the calling member. This is automatically populated by the compiler and should not be set manually.</param>
    /// <param name="path">The path of the source file of the caller. This is automatically populated by the compiler and should not be set manually.</param>
    public static void Info(string message,
        [CallerMemberName] string member = "", [CallerFilePath] string path = "")
        => Dispatch(LogLevel.Info, message, member, path);
    
    /// <summary>
    /// Logs a message with the <see cref="LogLevel.Warning"/> severity level.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="member">The name of the calling member. This is automatically populated by the compiler and should not be set manually.</param>
    /// <param name="path">The path of the source file of the caller. This is automatically populated by the compiler and should not be set manually.</param>
    public static void Warning(string message,
        [CallerMemberName] string member = "", [CallerFilePath] string path = "")
        => Dispatch(LogLevel.Warning, message, member, path);

    /// <summary>
    /// Logs a message with the <see cref="LogLevel.Error"/> severity level.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="member">The name of the calling member. This is automatically populated by the compiler and should not be set manually.</param>
    /// <param name="path">The path of the source file of the caller. This is automatically populated by the compiler and should not be set manually.</param>
    public static void Error(string message,
        [CallerMemberName] string member = "", [CallerFilePath] string path = "")
        => Dispatch(LogLevel.Error, message, member, path);
    
    /// <summary>
    /// Logs a message with the <see cref="LogLevel.Critical"/> severity level.
    /// </summary>
    /// <param name="message">The log message.</param>
    /// <param name="member">The name of the calling member. This is automatically populated by the compiler and should not be set manually.</param>
    /// <param name="path">The path of the source file of the caller. This is automatically populated by the compiler and should not be set manually.</param>
    public static void Critical(string message,
        [CallerMemberName] string member = "", [CallerFilePath] string path = "")
        => Dispatch(LogLevel.Critical, message, member, path);
}