using System;
using System.Buffers;
using System.Runtime.InteropServices;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Extensions.WebRtc.Apm.Components;

/// <summary>
/// Delegate for handling processed audio chunks.
/// </summary>
/// <param name="processedChunk">A ReadOnlyMemory of float representing the processed audio chunk.</param>
public delegate void ProcessedAudioChunkHandler(ReadOnlyMemory<float> processedChunk);

/// <summary>
/// Processes audio from an ISoundDataProvider using WebRTC Noise Suppression
/// and provides the cleaned audio either as a complete raw float array or
/// chunk by chunk via an event. This component is designed for offline/batch processing.
/// </summary>
public class NoiseSuppressor : IDisposable
{
    private readonly ISoundDataProvider _dataProvider;
    private readonly AudioProcessingModule _apm;
    private readonly ApmConfig _apmConfig;
    private readonly StreamConfig _inputStreamConfig;
    private readonly StreamConfig _outputStreamConfig;

    private readonly AudioFormat _audioFormat;
    private readonly int _apmFrameSizePerChannel; // Samples per channel for a 10ms APM frame
    private const int BytesPerSample = sizeof(float);

    private readonly float[][] _deinterleavedInputApmFrame;
    private readonly float[][] _deinterleavedOutputApmFrame;

    private readonly nint[] _inputChannelPtrs;
    private readonly nint[] _outputChannelPtrs;
    private readonly nint _inputChannelArrayPtr;
    private readonly nint _outputChannelArrayPtr;
    private GCHandle _inputChannelArrayHandle;
    private GCHandle _outputChannelArrayHandle;

    private bool _isDisposed;

    /// <summary>
    /// Event raised when a chunk of audio has been processed.
    /// </summary>
    public event ProcessedAudioChunkHandler? OnAudioChunkProcessed;

    /// <summary>
    /// Initializes a new instance of the <see cref="NoiseSuppressor"/> class.
    /// </summary>
    /// <param name="dataProvider">The audio data provider to process.</param>
    /// <param name="audioFormat">The format of the audio from the dataProvider. SampleRate must be 8k, 16k, 32k, or 48k Hz. Format must be F32.</param>
    /// <param name="suppressionLevel">The desired level of noise suppression.</param>
    /// <param name="useMultichannelProcessing">If true and audioFormat.Channels > 1, attempts to process channels independently. If false, channels may be downmixed by APM.</param>
    /// <exception cref="ArgumentNullException">Thrown if dataProvider is null.</exception>
    /// <exception cref="ArgumentException">Thrown if audioFormat is invalid or unsupported by WebRTC APM.</exception>
    public NoiseSuppressor(
        ISoundDataProvider dataProvider,
        AudioFormat audioFormat,
        NoiseSuppressionLevel suppressionLevel = NoiseSuppressionLevel.High,
        bool useMultichannelProcessing = false)
    {
        _dataProvider = dataProvider ?? throw new ArgumentNullException(nameof(dataProvider));
        _audioFormat = audioFormat;

        // Extract properties for easier use and validation
        var sampleRate = _audioFormat.SampleRate;
        var numChannels = _audioFormat.Channels;

        if (_audioFormat.Format != SampleFormat.F32)
            throw new ArgumentException("Audio format must be F32 (float) for WebRTC APM processing.", nameof(audioFormat));

        if (sampleRate != 8000 && sampleRate != 16000 && sampleRate != 32000 && sampleRate != 48000)
            throw new ArgumentException($"Unsupported sample rate for WebRTC Audio Processing Module: {sampleRate} Hz. Must be 8k, 16k, 32k, or 48k.", nameof(audioFormat));
        if (numChannels <= 0)
            throw new ArgumentException("Number of channels must be greater than 0.", nameof(audioFormat));

        _apmFrameSizePerChannel = AudioProcessingModule.GetFrameSize(sampleRate);
        if (_apmFrameSizePerChannel == 0)
            throw new ArgumentException($"Could not determine APM frame size for sample rate {sampleRate} Hz.", nameof(audioFormat));

        var apmFrameSizeBytesPerChannel = _apmFrameSizePerChannel * BytesPerSample;

        _apm = new AudioProcessingModule();
        _apmConfig = new ApmConfig();

        // Configure APM specifically for Noise Suppression
        _apmConfig.SetNoiseSuppression(true, suppressionLevel);
        _apmConfig.SetEchoCanceller(false, false);
        _apmConfig.SetGainController1(false, GainControlMode.FixedDigital, 0, 0, false);
        _apmConfig.SetGainController2(false);
        _apmConfig.SetHighPassFilter(false);
        _apmConfig.SetPreAmplifier(false, 1.0f);

        // Configure pipeline for multi-channel or mono processing
        var multiChannelFlag = useMultichannelProcessing && numChannels > 1;
        _apmConfig.SetPipeline(sampleRate, multiChannelFlag, multiChannelFlag, DownmixMethod.AverageChannels);

        var applyError = _apm.ApplyConfig(_apmConfig);
        if (applyError != ApmError.NoError)
        {
            _apm.Dispose();
            _apmConfig.Dispose();
            throw new InvalidOperationException($"Failed to apply APM config: {applyError}");
        }

        _inputStreamConfig = new StreamConfig(sampleRate, numChannels);
        _outputStreamConfig = new StreamConfig(sampleRate, numChannels);

        var initError = _apm.Initialize();
        if (initError != ApmError.NoError)
        {
            _apm.Dispose();
            _apmConfig.Dispose();
            _inputStreamConfig.Dispose();
            _outputStreamConfig.Dispose();
            throw new InvalidOperationException($"Failed to initialize APM: {initError}");
        }

        // Allocate managed and unmanaged buffers
        _deinterleavedInputApmFrame = new float[numChannels][];
        _deinterleavedOutputApmFrame = new float[numChannels][];
        _inputChannelPtrs = new nint[numChannels];
        _outputChannelPtrs = new nint[numChannels];

        try
        {
            for (var i = 0; i < numChannels; i++)
            {
                _deinterleavedInputApmFrame[i] = new float[_apmFrameSizePerChannel];
                _deinterleavedOutputApmFrame[i] = new float[_apmFrameSizePerChannel];
                _inputChannelPtrs[i] = Marshal.AllocHGlobal(apmFrameSizeBytesPerChannel);
                _outputChannelPtrs[i] = Marshal.AllocHGlobal(apmFrameSizeBytesPerChannel);
            }

            _inputChannelArrayHandle = GCHandle.Alloc(_inputChannelPtrs, GCHandleType.Pinned);
            _inputChannelArrayPtr = _inputChannelArrayHandle.AddrOfPinnedObject();
            _outputChannelArrayHandle = GCHandle.Alloc(_outputChannelPtrs, GCHandleType.Pinned);
            _outputChannelArrayPtr = _outputChannelArrayHandle.AddrOfPinnedObject();
        }
        catch
        {
            Log.Error("NoiseSuppressor: Failed to allocate managed and unmanaged buffers.");
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Processes the entire audio stream from the provided data provider
    /// and returns the noise-suppressed audio as a single float array.
    /// This method is suitable for smaller audio files that can fit in memory.
    /// </summary>
    /// <returns>A float array containing the processed (noise-suppressed) audio data.</returns>
    public float[] ProcessAll()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var processedAudioList = new List<float>();
        ProcessChunks(chunk => { processedAudioList.AddRange(chunk.ToArray()); });
        return [.. processedAudioList];
    }

    /// <summary>
    /// Processes the audio stream from the data provider in chunks.
    /// For each processed chunk, the <see cref="OnAudioChunkProcessed"/> event is raised.
    /// This method is suitable for processing large audio files without loading them entirely into memory.
    /// </summary>
    /// <param name="chunkHandler">An optional action to handle processed chunks directly, in addition to the event.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the NoiseSuppressor has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the processing failed or if the APM configuration is invalid.</exception>
    public void ProcessChunks(Action<ReadOnlyMemory<float>>? chunkHandler = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var samplesPerApmFrameInterleaved = _apmFrameSizePerChannel * _audioFormat.Channels;
        var providerReadBuffer = ArrayPool<float>.Shared.Rent(samplesPerApmFrameInterleaved);
        var processedFrameBuffer = ArrayPool<float>.Shared.Rent(samplesPerApmFrameInterleaved);

        try
        {
            int samplesActuallyReadFromProvider;
            do
            {
                // Read a full APM frame's worth of interleaved samples from the provider
                Array.Clear(providerReadBuffer, 0, samplesPerApmFrameInterleaved);
                samplesActuallyReadFromProvider = _dataProvider.ReadBytes(
                    providerReadBuffer.AsSpan(0, samplesPerApmFrameInterleaved));

                if (samplesActuallyReadFromProvider > 0)
                {
                    var currentFrameSpan = providerReadBuffer.AsSpan(0, samplesPerApmFrameInterleaved);

                    // If last chunk is smaller than APM frame, pad with silence
                    // Note: The input providerReadBuffer is already samplesPerApmFrameInterleaved long
                    // so we just need to clear the part that wasn't filled by ReadBytes if it's a partial read.
                    if (samplesActuallyReadFromProvider < samplesPerApmFrameInterleaved)
                    {
                        currentFrameSpan[samplesActuallyReadFromProvider..].Clear();
                    }

                    Deinterleave(currentFrameSpan, _audioFormat.Channels, _apmFrameSizePerChannel, _deinterleavedInputApmFrame);

                    for (var ch = 0; ch < _audioFormat.Channels; ch++)
                        Marshal.Copy(_deinterleavedInputApmFrame[ch], 0, _inputChannelPtrs[ch], _apmFrameSizePerChannel);

                    var error = NativeMethods.ProcessStream(
                        _apm.NativePtr,
                        _inputChannelArrayPtr,
                        _inputStreamConfig.NativePtr,
                        _outputStreamConfig.NativePtr,
                        _outputChannelArrayPtr);

                    if (error == ApmError.NoError)
                    {
                        for (var ch = 0; ch < _audioFormat.Channels; ch++)
                            Marshal.Copy(_outputChannelPtrs[ch], _deinterleavedOutputApmFrame[ch], 0, _apmFrameSizePerChannel);

                        Interleave(_deinterleavedOutputApmFrame, _audioFormat.Channels, _apmFrameSizePerChannel, processedFrameBuffer.AsSpan(0, samplesPerApmFrameInterleaved));

                        // Raise event/handler with the valid portion of the processed chunk
                        var validProcessedChunk = processedFrameBuffer.AsMemory(0, samplesActuallyReadFromProvider);
                        OnAudioChunkProcessed?.Invoke(validProcessedChunk);
                        chunkHandler?.Invoke(validProcessedChunk);
                    }
                    else
                    {
                        // On error, pass through the original chunk
                        var originalValidChunk = providerReadBuffer.AsMemory(0, samplesActuallyReadFromProvider);
                        OnAudioChunkProcessed?.Invoke(originalValidChunk);
                        chunkHandler?.Invoke(originalValidChunk);
                        Console.Error.WriteLine($"Noise suppression process failed: {error}. Passing through chunk.");
                        // Optionally throw or handle error differently
                    }
                }
            } while (samplesActuallyReadFromProvider == samplesPerApmFrameInterleaved);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(providerReadBuffer);
            ArrayPool<float>.Shared.Return(processedFrameBuffer);
        }
    }


    private static void Deinterleave(ReadOnlySpan<float> interleaved, int numChannels, int frameSizePerChannel, float[][] deinterleavedTarget)
    {
        for (var ch = 0; ch < numChannels; ch++)
        {
            for (var i = 0; i < frameSizePerChannel; i++)
            {
                var idx = i * numChannels + ch;
                deinterleavedTarget[ch][i] = idx < interleaved.Length ? interleaved[idx] : 0f;
            }
        }
    }

    private static void Interleave(float[][] deinterleaved, int numChannels, int frameSizePerChannel, Span<float> interleavedTarget)
    {
        for (var ch = 0; ch < numChannels; ch++)
        {
            for (var i = 0; i < frameSizePerChannel; i++)
            {
                var idx = i * numChannels + ch;
                if (idx < interleavedTarget.Length) interleavedTarget[idx] = deinterleaved[ch][i];
            }
        }
    }

    /// <summary>
    /// Disposes this NoiseSuppressor instance.
    /// </summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            if (_inputChannelArrayHandle.IsAllocated) _inputChannelArrayHandle.Free();
            if (_outputChannelArrayHandle.IsAllocated) _outputChannelArrayHandle.Free();

            for (var i = 0; i < _audioFormat.Channels; i++)
                if (_inputChannelPtrs[i] != nint.Zero) Marshal.FreeHGlobal(_inputChannelPtrs[i]);
            for (var i = 0; i < _audioFormat.Channels; i++)
                if (_outputChannelPtrs[i] != nint.Zero) Marshal.FreeHGlobal(_outputChannelPtrs[i]);

            _apm.Dispose();
            _apmConfig.Dispose();
            _inputStreamConfig.Dispose();
            _outputStreamConfig.Dispose();

            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <inheritdoc />
    ~NoiseSuppressor()
    {
        Dispose();
    }
}