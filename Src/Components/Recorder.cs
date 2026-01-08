using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata;
using SoundFlow.Metadata.Models;
using SoundFlow.Security;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;
using System.Collections.ObjectModel;

namespace SoundFlow.Components;

/// <summary>
/// Component for recording audio data from a capture device, either to a stream or via a callback.
/// Supports various sample and encoding formats and can integrate with <see cref="SoundModifier"/> and <see cref="AudioAnalyzer"/> components for real-time processing and analysis during recording.
/// Implements the <see cref="IDisposable"/> interface to ensure resources are released properly.
/// </summary>
public class Recorder : IDisposable
{
    /// <summary>
    /// Gets the current playback state of the recorder.
    /// </summary>
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    /// <summary>
    /// Gets the sample format used for recording.
    /// </summary>
    public readonly SampleFormat SampleFormat;

    /// <summary>
    /// Gets the format identifier (e.g., "wav", "flac") used for encoding.
    /// </summary>
    public readonly string FormatId;

    /// <summary>
    /// Gets the sample rate used for recording, in samples per second.
    /// </summary>
    public readonly int SampleRate;

    /// <summary>
    /// Gets the number of channels being recorded (e.g., 1 for mono, 2 for stereo).
    /// </summary>
    public readonly int Channels;

    /// <summary>
    /// Gets the file path where audio will be recorded, if recording to a file.
    /// Will be an empty string if recording via a callback.
    /// </summary>
    public readonly Stream Stream = Stream.Null;

    /// <summary>
    /// Gets the final destination file path where audio will be recorded.
    /// Will be null if recording via a callback or stream.
    /// </summary>
    public readonly string? FilePath;

    /// <summary>
    /// Gets or sets the callback function to be invoked when audio data is processed.
    /// This is used when recording directly to memory or for custom processing, instead of to a file.
    /// </summary>
    public AudioProcessCallback? ProcessCallback;
    
    /// <summary>
    /// Gets or sets the configuration for digitally signing the recorded file.
    /// If set, a detached signature file (.sig) will be generated upon stopping the recording.
    /// Only applies when recording to a file.
    /// </summary>
    public SignatureConfiguration? SigningConfiguration { get; set; }
    
    private readonly AudioCaptureDevice _captureDevice;
    private ISoundEncoder? _encoder;
    private readonly List<SoundModifier> _modifiers = [];
    private readonly List<AudioAnalyzer> _analyzers = [];
    private readonly AudioEngine _engine;
    private readonly AudioFormat _format;
    
    private SoundTags? _tagsToWrite;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="Recorder"/> class to record audio to a file.
    /// </summary>
    /// <param name="captureDevice">The capture device to record from.</param>
    /// <param name="filePath">The final destination path for the recorded audio file.</param>
    /// <param name="formatId">The string identifier for the desired encoding format (e.g., "wav", "flac"). Defaults to "wav".</param>
    public Recorder(AudioCaptureDevice captureDevice, string filePath, string formatId = "wav") : this(captureDevice, new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None), formatId)
    {
        FilePath = filePath;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Recorder"/> class to record audio to a stream.
    /// </summary>
    /// <param name="captureDevice">The capture device to record from.</param>
    /// <param name="stream">The stream to write encoded recorded audio to, disposed when recording stops.</param>
    /// <param name="formatId">The string identifier for the desired encoding format (e.g., "wav", "flac"). Defaults to "wav".</param>
    /// <exception cref="ArgumentException">Thrown if the provided stream is not writable.</exception>
    public Recorder(AudioCaptureDevice captureDevice, Stream stream, string formatId = "wav")
    {
        if (!stream.CanWrite)
        {
            throw new ArgumentException("The provided stream is not writable.", nameof(stream));
        }
        
        _captureDevice = captureDevice;
        _engine = captureDevice.Engine;
        SampleFormat = captureDevice.Format.Format;
        FormatId = formatId;
        Stream = stream;
        SampleRate = captureDevice.Format.SampleRate;
        Channels = captureDevice.Format.Channels;
        _format = captureDevice.Format;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Recorder"/> class to record audio and process it via a callback function.
    /// </summary>
    /// <param name="captureDevice">The capture device to record from.</param>
    /// <param name="callback">The callback function to be invoked when audio data is processed. This function should handle the recorded audio data.</param>
    public Recorder(AudioCaptureDevice captureDevice, AudioProcessCallback callback)
    {
        _captureDevice = captureDevice;
        _engine = captureDevice.Engine;
        ProcessCallback = callback;
        SampleFormat = captureDevice.Format.Format;
        SampleRate = captureDevice.Format.SampleRate;
        Channels = captureDevice.Format.Channels;
        FormatId = string.Empty; // No encoding format needed for callback mode.
        _format = captureDevice.Format;
    }
    
    /// <summary>
    /// Gets a read-only list of <see cref="SoundModifier"/> components applied to the recorder.
    /// </summary>
    public ReadOnlyCollection<SoundModifier> Modifiers => _modifiers.AsReadOnly();

    /// <summary>
    /// Gets a read-only list of <see cref="AudioAnalyzer"/> components applied to the recorder.
    /// </summary>
    public ReadOnlyCollection<AudioAnalyzer> Analyzers => _analyzers.AsReadOnly();

    /// <summary>
    /// Starts the audio recording process.
    /// If recording to a file or stream, it initializes an audio encoder.
    /// </summary>
    /// <param name="tags">Optional metadata tags to write to the file upon completion of the recording.</param>
    /// <returns>A <see cref="Result"/> indicating success or failure.</returns>
    public Result StartRecording(SoundTags? tags = null)
    {
        if ((Stream == Stream.Null || !Stream.CanWrite) && ProcessCallback == null)
            return new ValidationError("A valid writable stream or callback must be provided.");

        if (State == PlaybackState.Playing) return Result.Fail(new DuplicateRequestError("Starting recording"));

        _tagsToWrite = tags;
        if (!string.IsNullOrEmpty(FormatId))
        {
            try
            {
                _encoder = _engine.CreateEncoder(Stream, FormatId, _format);
            }
            catch (Exception ex)
            {
                return new InvalidOperationError($"Failed to create audio encoder for format '{FormatId}'.", ex);
            }
        }

        _captureDevice.OnAudioProcessed += OnAudioProcessed;
        State = PlaybackState.Playing;
        return Result.Ok();
    }

    /// <summary>
    /// Resumes recording from a paused state.
    /// Has no effect if the recorder is not in the <see cref="PlaybackState.Paused"/> state.
    /// </summary>
    /// <returns>A <see cref="Result"/> indicating success.</returns>
    public Result ResumeRecording()
    {
        if (State != PlaybackState.Paused)
            return Result.Fail(new DuplicateRequestError("Resuming recording"));

        State = PlaybackState.Playing;
        return Result.Ok();
    }

    /// <summary>
    /// Pauses the recording process.
    /// Audio data is no longer processed or encoded until recording is resumed.
    /// Has no effect if the recorder is not in the <see cref="PlaybackState.Playing"/> state.
    /// </summary>
    /// <returns>A <see cref="Result"/> indicating success.</returns>
    public Result PauseRecording()
    {
        if (State != PlaybackState.Playing)
            return Result.Fail(new DuplicateRequestError("Pausing recording"));

        State = PlaybackState.Paused;
        return Result.Ok();
    }

    /// <summary>
    /// Stops the recording process and releases resources.
    /// If recording to a file, it finalizes the encoding process, writes metadata tags if provided,
    /// and generates a digital signature if configured.
    /// </summary>
    /// <returns>A <see cref="Result"/> indicating success or failure.</returns>
    public async Task<Result> StopRecordingAsync()
    {
        if (State == PlaybackState.Stopped)
            return Result.Fail(new DuplicateRequestError("Stopping recording"));

        _captureDevice.OnAudioProcessed -= OnAudioProcessed;

        _encoder?.Dispose();
        _encoder = null;
        State = PlaybackState.Stopped;
        
        try
        {
            await Stream.DisposeAsync();
        }
        catch (Exception ex)
        {
            return new IoError("Disposing the underlying stream", ex);
        }

        try
        {
            if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
            {
                // 1. Write Tags
                if (_tagsToWrite != null)
                {
                    try
                    {
                        await SoundMetadataWriter.WriteTagsAsync(FilePath, _tagsToWrite);
                    }
                    catch (Exception ex)
                    {
                        return new IoError($"writing metadata tags to '{FilePath}'", ex);
                    }
                }

                // 2. Sign File (Authentic Recording)
                if (SigningConfiguration != null)
                {
                    var signResult = await FileAuthenticator.SignFileAsync(FilePath, SigningConfiguration);
                    if (signResult is { IsFailure: true, Error: not null })
                    {
                        return Result.Fail(signResult.Error);
                    }

                    var sigPath = FilePath + ".sig";
                    try
                    {
                        await File.WriteAllTextAsync(sigPath, signResult.Value);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        return new AccessDeniedError(sigPath, ex);
                    }
                    catch (IOException ex)
                    {
                        return new IoError($"writing signature file to '{sigPath}'", ex);
                    }
                }
            }
        }
        finally
        {
            _tagsToWrite = null;
        }
        
        return Result.Ok();
    }
    
    /// <summary>
    /// Synchronously stops the recording process.
    /// </summary>
    /// <returns>A <see cref="Result"/> indicating success or failure.</returns>
    public Result StopRecording() => StopRecordingAsync().GetAwaiter().GetResult();

    /// <summary>
    /// Adds a <see cref="SoundModifier"/> to the recording pipeline.
    /// Modifiers are applied to the audio data before encoding or processing via callback.
    /// </summary>
    /// <param name="modifier">The modifier to add.</param>
    public void AddModifier(SoundModifier modifier)
    {
        _modifiers.Add(modifier);
    }

    /// <summary>
    /// Removes a <see cref="SoundModifier"/> from the recording pipeline.
    /// </summary>
    /// <param name="modifier">The modifier to remove.</param>
    public void RemoveModifier(SoundModifier modifier)
    {
        _modifiers.Remove(modifier);
    }

    /// <summary>
    /// Adds an <see cref="AudioAnalyzer"/> to the recording pipeline.
    /// Analyzers can be used to process and extract data from the audio during recording.
    /// </summary>
    /// <param name="analyzer">The analyzer to add.</param>
    public void AddAnalyzer(AudioAnalyzer analyzer)
    {
        _analyzers.Add(analyzer);
    }

    /// <summary>
    /// Removes an <see cref="AudioAnalyzer"/> from the recording pipeline.
    /// </summary>
    /// <param name="analyzer">The analyzer to remove.</param>
    public void RemoveAnalyzer(AudioAnalyzer analyzer)
    {
        _analyzers.Remove(analyzer);
    }

    /// <summary>
    /// Handles the audio processed event from the audio engine.
    /// This method is invoked by the audio engine when new audio samples are available.
    /// It processes the samples through the added <see cref="SoundModifier"/> and <see cref="AudioAnalyzer"/> components, checks the current state, invokes the <see cref="ProcessCallback"/> (if set), and encodes the samples using the <see cref="_encoder"/> (if recording to a file).
    /// </summary>
    /// <param name="samples">A span containing the audio samples to process.</param>
    /// <param name="capability">The audio capability associated with the processed samples (e.g., input or output).</param>
    private void OnAudioProcessed(Span<float> samples, Capability capability)
    {
        if (State != PlaybackState.Playing)
            return;

        // Apply modifiers
        foreach (var modifier in _modifiers)
        {
            modifier.Process(samples, Channels);
        }

        // Process analyzers
        foreach (var analyzer in _analyzers)
        {
             analyzer.Process(samples, Channels);
        }

        // Pass samples
        ProcessCallback?.Invoke(samples, capability);
        _encoder?.Encode(samples);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (State != PlaybackState.Stopped) StopRecording();
        _captureDevice.OnAudioProcessed -= OnAudioProcessed;
        ProcessCallback = null;
        _modifiers.Clear();
        _analyzers.Clear();
        GC.SuppressFinalize(this);
    }
}