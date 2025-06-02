using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Exceptions;
using System.Collections.ObjectModel;

namespace SoundFlow.Components;

/// <summary>
/// Component for recording audio data, either to a file or via a callback.
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
    /// Gets the encoding format used for recording.
    /// </summary>
    public readonly EncodingFormat EncodingFormat;

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
    /// Gets or sets the callback function to be invoked when audio data is processed.
    /// This is used when recording directly to memory or for custom processing, instead of to a file.
    /// </summary>
    public AudioProcessCallback? ProcessCallback;

    private ISoundEncoder? _encoder;
    private readonly List<SoundModifier> _modifiers = [];
    private readonly List<AudioAnalyzer> _analyzers = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="Recorder"/> class to record audio to a file.
    /// </summary>
    /// <param name="stream">The stream to write encoded recorded audio to.</param>
    /// <param name="sampleFormat">The desired sample format for recording. Defaults to <see cref="SampleFormat.F32"/>.</param>
    /// <param name="encodingFormat">The desired encoding format for the recorded audio file. Defaults to <see cref="EncodingFormat.Wav"/>.</param>
    /// <param name="sampleRate">The desired sample rate for recording, in samples per second. Defaults to 44100 Hz.</param>
    /// <param name="channels">The number of channels to record. Defaults to 2 (stereo).</param>
    public Recorder(Stream stream,
        SampleFormat sampleFormat = SampleFormat.F32,
        EncodingFormat encodingFormat = EncodingFormat.Wav,
        int sampleRate = 44100,
        int channels = 2)
    {
        SampleFormat = sampleFormat;
        EncodingFormat = encodingFormat;
        Stream = stream;
        SampleRate = sampleRate;
        Channels = channels;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Recorder"/> class to record audio and process it via a callback function.
    /// </summary>
    /// <param name="callback">The callback function to be invoked when audio data is processed. This function should handle the recorded audio data.</param>
    /// <param name="sampleFormat">The desired sample format for recording. Defaults to <see cref="SampleFormat.F32"/>.</param>
    /// <param name="encodingFormat">The encoding format (primarily for internal use or if an encoder is manually managed). Defaults to <see cref="EncodingFormat.Wav"/>.</param>
    /// <param name="sampleRate">The desired sample rate for recording, in samples per second. Defaults to 44100 Hz.</param>
    /// <param name="channels">The number of channels to record. Defaults to 2 (stereo).</param>
    public Recorder(AudioProcessCallback callback,
        SampleFormat sampleFormat = SampleFormat.F32,
        EncodingFormat encodingFormat = EncodingFormat.Wav,
        int sampleRate = 44100,
        int channels = 2)
    {
        ProcessCallback = callback;
        SampleFormat = sampleFormat;
        EncodingFormat = encodingFormat;
        SampleRate = sampleRate;
        Channels = channels;
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
    /// If recording to a file, it initializes the audio encoder.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown if both <see cref="Stream"/> and <see cref="ProcessCallback"/> are invalid (e.g., <see cref="Stream"/> is null or empty and <see cref="ProcessCallback"/> is null).</exception>
    /// <exception cref="BackendException">Thrown if creating the audio encoder fails when recording to a file.</exception>
    public void StartRecording()
    {
        if (ProcessCallback == null && (Stream == Stream.Null || !Stream.CanWrite))
            throw new ArgumentException("Invalid stream or callback", nameof(Stream));

        if (State == PlaybackState.Playing)
            return;

        _encoder = AudioEngine.Instance.CreateEncoder(Stream, EncodingFormat, SampleFormat, Channels, SampleRate);
        if (_encoder == null)
            throw new BackendException(AudioEngine.Instance.GetType().Name, Result.Error,
                "Failed to create encoder.");

        AudioEngine.OnAudioProcessed += OnOnAudioProcessed;
        State = PlaybackState.Playing;
    }

    /// <summary>
    /// Resumes recording from a paused state.
    /// Has no effect if the recorder is not in the <see cref="PlaybackState.Paused"/> state.
    /// </summary>
    public void ResumeRecording()
    {
        if (State != PlaybackState.Paused)
            return;

        State = PlaybackState.Playing;
    }

    /// <summary>
    /// Pauses the recording process.
    /// Audio data is no longer processed or encoded until recording is resumed.
    /// Has no effect if the recorder is not in the <see cref="PlaybackState.Playing"/> state.
    /// </summary>
    public void PauseRecording()
    {
        if (State != PlaybackState.Playing)
            return;

        State = PlaybackState.Paused;
    }

    /// <summary>
    /// Stops the recording process and releases resources.
    /// If recording to a file, it finalizes the encoding process and closes the file.
    /// Detaches from the audio processing engine and sets the state to <see cref="PlaybackState.Stopped"/>.
    /// </summary>
    public void StopRecording()
    {
        if (State == PlaybackState.Stopped)
            return;

        AudioEngine.OnAudioProcessed -= OnOnAudioProcessed;

        _encoder?.Dispose();
        _encoder = null;
        State = PlaybackState.Stopped;
    }

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
    private void OnOnAudioProcessed(Span<float> samples, Capability capability)
    {
        if (State != PlaybackState.Playing)
            return;

        // Apply modifiers
        foreach (var modifier in _modifiers)
        {
            modifier.Process(samples);
        }

        // Process analyzers
        foreach (var analyzer in _analyzers)
        {
             analyzer.Process(samples);
        }

        // Pass samples
        ProcessCallback?.Invoke(samples, capability);
        _encoder?.Encode(samples);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopRecording();
        AudioEngine.OnAudioProcessed -= OnOnAudioProcessed;
        ProcessCallback = null;
        _modifiers.Clear();
        _analyzers.Clear();
        GC.SuppressFinalize(this);
    }
}