using SoundFlow.Enums;
using SoundFlow.Metadata.Models;

namespace SoundFlow.Interfaces;

/// <summary>
///     Interface for a sound data provider.
/// </summary>
public interface ISoundDataProvider : IDisposable
{
    /// <summary>
    ///     Gets the current playback position in samples.
    /// </summary>
    int Position { get; }

    /// <summary>
    ///     Gets the total length of the audio data in samples.
    ///     May be 0 or -1 if the length is unknown (e.g., for streaming audio).
    /// </summary>
    int Length { get; }

    /// <summary>
    ///     Gets a value indicating whether the data source supports seeking.
    /// </summary>
    bool CanSeek { get; }

    /// <summary>
    ///     Gets the format of the audio samples.
    /// </summary>
    SampleFormat SampleFormat { get; }

    /// <summary>
    ///     Gets or sets the target sample rate of the audio data.
    /// </summary>
    int SampleRate { get; }
    
    /// <summary>
    ///     Gets a value indicating whether the data source has been disposed.
    /// </summary>
    bool IsDisposed { get; }
    
    /// <summary>
    /// Gets the detailed format and tag information read from the source file.
    /// This will be null if the provider is not sourced from a file with metadata (e.g., raw data, microphone).
    /// </summary>
    SoundFormatInfo? FormatInfo { get; }

    /// <summary>
    ///     Reads the specified number of audio bytes into the given buffer asynchronously.
    /// </summary>
    /// <param name="buffer">The buffer to write the bytes to.</param>
    /// <returns>
    ///     A task representing the asynchronous read operation. The task result contains the number of bytes actually
    ///     read. May be less than the requested number if the end of the data is reached.
    /// </returns>
    int ReadBytes(Span<float> buffer);

    /// <summary>
    ///     Sets the playback position to the specified sample offset.
    /// </summary>
    /// <param name="offset">The sample offset to seek to.</param>
    void Seek(int offset);

    /// <summary>
    ///     Raised when the end of the audio stream is reached.
    /// </summary>
    event EventHandler<EventArgs> EndOfStreamReached;

    /// <summary>
    ///     Raised when the playback position changes.
    /// </summary>
    event EventHandler<PositionChangedEventArgs> PositionChanged;
}

/// <summary>
///     Event arguments for the PositionChanged event.
/// </summary>
public class PositionChangedEventArgs : EventArgs
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PositionChangedEventArgs" /> class.
    /// </summary>
    /// <param name="newPosition">The new playback position in samples.</param>
    public PositionChangedEventArgs(int newPosition)
    {
        NewPosition = newPosition;
    }

    /// <summary>
    ///     Gets the new playback position in samples.
    /// </summary>
    public int NewPosition { get; }
}