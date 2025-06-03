using System.Buffers;
using System.Runtime.CompilerServices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Exceptions;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Abstracts;

/// <summary>
///     This event is raised when samples are processed by Input or Output components.
/// </summary>
public delegate void AudioProcessCallback(Span<float> samples, Capability capability);

/// <summary>
///     The base class for audio engines.
///     This class provides common functionality for audio engines such as sample rate and
///     channel count management, thread management, and solo/mute functionality.
/// </summary>
public abstract class AudioEngine : IDisposable
{
    private static AudioEngine? _instance;

    private bool _soloedComponentExists;
    private SoundComponent? _soloedComponent;

    private readonly object _lock = new();

    private Thread? _audioThread;
    private readonly ManualResetEvent _audioThreadStarted = new(false);
    private readonly ManualResetEvent _stopAudioThread = new(false);

    /// <summary>
    ///     Constructs a new instance of <see cref="AudioEngine" />.
    /// </summary>
    /// <param name="sampleRate">The sampling rate used by the engine.</param>
    /// <param name="capability">The audio device capability.</param>
    /// <param name="sampleFormat">The sample format used by the engine.</param>
    /// <param name="channels">The number of audio channels.</param>
    protected AudioEngine(int sampleRate, Capability capability, SampleFormat sampleFormat, int channels)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "Sample rate must be positive.");
        if (_instance != null)
            throw new InvalidOperationException("An instance of AudioEngine is already initialized.");

        _instance = this;
        SampleRate = sampleRate;
        InverseSampleRate = 1f / sampleRate;
        Channels = channels;
        Capability = capability;
        SampleFormat = sampleFormat;

        SetupAudioThread();
    }

    private void SetupAudioThread()
    {
        // Start audio device in a dedicated thread
        if (RequiresBackendThread)
        {
            _audioThread = new Thread(AudioThreadLoop);
            _audioThread.Start();
            _audioThreadStarted.WaitOne();
        }
        else
        {
            InitializeAudioDevice();
        }
    }

    private void AudioThreadLoop()
    {
        // Backend-specific initialization that requires a separate thread
        InitializeAudioDevice();

        _audioThreadStarted.Set();

        while (!_stopAudioThread.WaitOne(0))
        {
            // Process audio data in a loop
            ProcessAudioData();
        }

        // Backend-specific cleanup
        CleanupAudioDevice();
    }

    /// <summary>
    ///     Initializes the audio device and starts the audio processing loop.
    /// </summary>
    protected abstract void InitializeAudioDevice(); // Backend-specific initialization

    /// <summary>
    ///     Processes audio data in a loop.
    /// </summary>
    protected abstract void ProcessAudioData(); // Backend-specific audio processing loop

    /// <summary>
    ///     Cleans up the audio device.
    /// </summary>
    protected abstract void CleanupAudioDevice(); // Backend-specific cleanup

    /// <summary>
    ///     Gets the configured sample format.
    /// </summary>
    public SampleFormat SampleFormat { get; }

    /// <summary>
    ///     Gets the configured sample rate (samples per second).
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    ///     Gets the inverse of the configured sample rate (seconds per sample).
    /// </summary>
    public float InverseSampleRate { get; }

    /// <summary>
    ///     Gets the number of configured audio channels.
    /// </summary>
    public static int Channels { get; private set; } = 2; // Stereo by default

    /// <summary>
    ///     Gets or sets the capability of the audio engine.
    /// </summary>
    public Capability Capability { get; }

    /// <summary>
    ///     Gets whether the backend requires a dedicated thread for audio processing.
    ///     True if <see cref="AudioEngine"/> manages a backend thread, false otherwise.
    ///     Subclasses override to indicate backend threading requirements.
    /// </summary>
    protected virtual bool RequiresBackendThread { get; } = true;

    /// <summary>
    ///     Gets the currently selected playback device.
    /// </summary>
    public DeviceInfo? CurrentPlaybackDevice { get; protected set; }

    /// <summary>
    ///     Gets the currently selected capture device.
    /// </summary>
    public DeviceInfo? CurrentCaptureDevice { get; protected set; }

    /// <summary>
    ///     Gets the number of available capture devices.
    /// </summary>
    public int CaptureDeviceCount { get; protected set; }

    /// <summary>
    ///     Gets the number of available playback devices.
    /// </summary>
    public int PlaybackDeviceCount { get; protected set; }

    /// <summary>
    ///     Gets an array of available playback devices.
    /// </summary>
    public DeviceInfo[] PlaybackDevices { get; protected set; } = [];

    /// <summary>
    ///     Gets an array of available capture devices.
    /// </summary>
    public DeviceInfo[] CaptureDevices { get; protected set; } = [];

    /// <summary>
    ///     Gets the audio engine instance.
    /// </summary>
    public static AudioEngine Instance => _instance ?? throw new BackendException("None", Result.NoBackend,
        "AudioEngine is not initialized yet. Create an instance of your backend first.");

    /// <summary>
    ///     Cleans up resources before the object is garbage collected.
    /// </summary>
    ~AudioEngine()
    {
        Dispose(false);
    }

    /// <summary>
    ///     Solos the specified sound component, muting all other components.
    /// </summary>
    /// <param name="component">The component to solo.</param>
    public void SoloComponent(SoundComponent component)
    {
        lock (_lock)
        {
            _soloedComponentExists = true;
            _soloedComponent = component;
        }
    }

    /// <summary>
    ///     Unsolos the specified sound component.
    /// </summary>
    /// <param name="component">The component to unsolo.</param>
    public void UnsoloComponent(SoundComponent component)
    {
        lock (_lock)
        {
            if (_soloedComponent == component)
            {
                _soloedComponentExists = false;
                _soloedComponent = null;
            }
        }
    }

    /// <summary>
    ///     Processes the audio graph synchronously.
    /// </summary>
    /// <param name="output">A pointer to the output buffer.</param>
    /// <param name="length">The length of the output buffer in samples.</param>
    protected void ProcessGraph(nint output, int length)
    {
        if (length <= 0 || output == nint.Zero)
            return;

        // Use a local variable to store the processed float samples
        float[]? tempBuffer = null;
        Span<float> buffer;

        if (SampleFormat == SampleFormat.F32)
            buffer = Extensions.GetSpan<float>(output, length);
        else
        {
            tempBuffer = ArrayPool<float>.Shared.Rent(length);
            buffer = tempBuffer.AsSpan(0, length);
        }


        // Process soloed component or the entire graph
        lock (_lock)
        {
            if (_soloedComponentExists)
                _soloedComponent?.Process(buffer);
            else
                Mixer.Master.Process(buffer);
        }

        // Handle output based on sample format
        switch (SampleFormat)
        {
            case SampleFormat.S16:
                ConvertAndCopyToOutput<short>(output, length, buffer, short.MaxValue);
                break;
            case SampleFormat.S24: // For Signed 24-bit PCM, using int for conversion
                ConvertAndCopyToOutputS24(output, length, buffer);
                break;
            case SampleFormat.S32:
                ConvertAndCopyToOutput<int>(output, length, buffer, int.MaxValue);
                break;
            case SampleFormat.U8: // Unsigned 8-bit PCM
                ConvertAndCopyToOutput<byte>(output, length, buffer, 128, true);
                break;
            case SampleFormat.F32:
                break;
            default:
                throw new NotSupportedException($"Sample format {SampleFormat} is not supported.");
        }

        OnAudioProcessed?.Invoke(buffer, Capability.Playback);

        if (tempBuffer != null)
            ArrayPool<float>.Shared.Return(tempBuffer);
    }

    private static void ConvertAndCopyToOutputS24(nint output, int length, Span<float> floatBuffer)
    {
        // Get a span of bytes representing the output buffer
        var outputSpan = Extensions.GetSpan<byte>(output, length * 3); // 3 bytes per S24 sample

        for (int i = 0, j = 0; i < length; i++, j += 3)
        {
            var clipped = Math.Clamp(floatBuffer[i], -1f, 1f);
            var sample24 = (int)(clipped * 8388607); // 2^23 - 1  (maximum positive value for 24-bit signed)

            // Manually pack the 24-bit sample into 3 bytes (Little Endian)
            outputSpan[j] = (byte)(sample24 & 0xFF);
            outputSpan[j + 1] = (byte)((sample24 >> 8) & 0xFF);
            outputSpan[j + 2] = (byte)((sample24 >> 16) & 0xFF);

            floatBuffer[i] = 0;
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ConvertAndCopyToOutput<T>(nint output, int length, Span<float> floatBuffer, float maxValue, bool isUnsigned = false) where T : unmanaged
    {
        // Parameter validation
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length == 0) return;
        if (float.IsNaN(maxValue) || float.IsInfinity(maxValue))
            throw new ArgumentException("maxValue must be a finite number", nameof(maxValue));
        if (output == nint.Zero) throw new ArgumentNullException(nameof(output));

        var outputSpan = Extensions.GetSpan<T>(output, length);

        try
        {
            if (typeof(T) == typeof(byte))
            {
                var unsignedOffset = isUnsigned ? maxValue : 0f;
                const float scale = byte.MaxValue;
                for (var i = 0; i < length; i++)
                {
                    var sample = floatBuffer[i];
                    if (!float.IsFinite(sample))
                    {
                        Unsafe.As<T, byte>(ref outputSpan[i]) = (byte)(scale * 0.5f);
                        continue;
                    }
                    var clipped = Math.Clamp(sample, -1f, 1f);
                    var scaled = clipped * maxValue + unsignedOffset;
                    Unsafe.As<T, byte>(ref outputSpan[i]) = (byte)Math.Clamp(scaled, 0f, scale);
                }
            }
            else if (typeof(T) == typeof(short))
            {
                var unsignedOffset = isUnsigned ? maxValue : 0f;
                float min = isUnsigned ? 0 : short.MinValue;
                const float max = short.MaxValue;
                for (var i = 0; i < length; i++)
                {
                    var sample = floatBuffer[i];
                    if (!float.IsFinite(sample))
                    {
                        Unsafe.As<T, short>(ref outputSpan[i]) = (short)(isUnsigned ? max * 0.5f : 0);
                        continue;
                    }
                    var clipped = Math.Clamp(sample, -1f, 1f);
                    var scaled = clipped * maxValue + unsignedOffset;
                    Unsafe.As<T, short>(ref outputSpan[i]) = (short)Math.Clamp(scaled, min, max);
                }
            }
            else if (typeof(T) == typeof(int))
            {
                var unsignedOffset = isUnsigned ? maxValue : 0f;
                float min = isUnsigned ? 0 : int.MinValue;
                const float max = int.MaxValue;
                for (var i = 0; i < length; i++)
                {
                    var sample = floatBuffer[i];
                    if (!float.IsFinite(sample))
                    {
                        Unsafe.As<T, int>(ref outputSpan[i]) = (int)(isUnsigned ? max * 0.5f : 0);
                        continue;
                    }
                    var clipped = Math.Clamp(sample, -1f, 1f);
                    var scaled = clipped * maxValue + unsignedOffset;
                    Unsafe.As<T, int>(ref outputSpan[i]) = (int)Math.Clamp(scaled, min, max);
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported output format: {typeof(T)}");
            }
        }
        finally
        {
            floatBuffer.Clear();
        }
    }


    /// <summary>
    ///     Called by an implementation when the audio input has captured samples.
    /// </summary>
    /// <param name="input">A pointer to the input buffer.</param>
    /// <param name="length">The length of the input buffer in samples.</param>
    protected void ProcessAudioInput(nint input, int length)
    {
        if (length <= 0 || input == nint.Zero)
            return;

        var inputBuffer = Extensions.GetSpan<float>(input, length);
        OnAudioProcessed?.Invoke(inputBuffer, Capability.Record);
    }

    /// <summary>
    ///     Constructs a sound encoder specific to the implementation.
    /// </summary>
    /// <param name="stream">The stream to write encoded audio to.</param>
    /// <param name="encodingFormat">The desired audio encoding format.</param>
    /// <param name="sampleFormat">The format of the input audio samples.</param>
    /// <param name="channels">The number of audio channels.</param>
    /// <param name="sampleRate">The sample rate of the input audio.</param>
    /// <returns>An instance of a sound encoder.</returns>
    public abstract ISoundEncoder CreateEncoder(Stream stream, EncodingFormat encodingFormat,
        SampleFormat sampleFormat, int channels, int sampleRate);

    /// <summary>
    ///     Constructs a sound decoder specific to the implementation.
    /// </summary>
    /// <param name="stream">The stream containing the audio data.</param>
    /// <returns>An instance of a sound decoder.</returns>
    public abstract ISoundDecoder CreateDecoder(Stream stream);

    /// <summary>
    ///     Switches the audio engine to use the specified device.
    /// </summary>
    /// <param name="deviceInfo">The device info of the device to switch to.</param>
    /// <param name="type">The type of device.</param>
    public abstract void SwitchDevice(DeviceInfo deviceInfo, DeviceType type = DeviceType.Playback);
    
    /// <summary>
    /// Switches the audio engine to the given playback and/or capture devices.
    /// </summary>
    /// <param name="playbackDeviceInfo">The playback device to switch to. <c>null</c> to keep the current playback device.</param>
    /// <param name="captureDeviceInfo">The capture device to switch to. <c>null</c> to keep the current capture device.</param>
    public abstract void SwitchDevices(DeviceInfo? playbackDeviceInfo, DeviceInfo? captureDeviceInfo);

    /// <summary>
    ///     Retrieves the list of available playback and capture devices from the underlying audio backend.
    /// </summary>
    /// <remarks>
    ///     This method should be called after any changes to the audio device configuration.
    /// </remarks>
    public abstract void UpdateDevicesInfo();
    
    /// <summary>
    ///     Occurs when samples are processed by Input or Output components.
    /// </summary>
    public static event AudioProcessCallback? OnAudioProcessed;

    #region IDisposable Support

    /// <summary>
    ///     Gets a value indicating whether the audio engine has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    ///     Disposes of managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (RequiresBackendThread)
        {
            _stopAudioThread.Set();
            _audioThread?.Join();
        }

        _instance = null;
        IsDisposed = true;
    }

    /// <summary>
    ///     Disposes of the audio engine.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}