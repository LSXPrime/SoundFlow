using System.Reflection;
using System.Runtime.InteropServices;

namespace SoundFlow.Extensions.WebRtc.Apm;

#pragma warning disable CS1591

/// <summary>
/// Error codes returned by the WebRTC Audio Processing Module
/// </summary>
public enum ApmError
{
    NoError = 0,
    UnspecifiedError = -1,
    CreationFailed = -2,
    BadParameter = -6,
    BadSampleRate = -7,
    BadDataLength = -8,
    BadNumChannels = -9
}

/// <summary>
/// Noise suppression levels
/// </summary>
public enum NoiseSuppressionLevel
{
    Low,
    Moderate,
    High,
    VeryHigh
}

/// <summary>
/// Gain controller modes
/// </summary>
public enum GainControlMode
{
    AdaptiveAnalog,
    AdaptiveDigital,
    FixedDigital
}

/// <summary>
/// Downmix methods
/// </summary>
public enum DownmixMethod
{
    AverageChannels,
    UseFirstChannel
}

/// <summary>
/// Runtime setting types
/// </summary>
public enum RuntimeSettingType
{
    CapturePreGain,
    CaptureCompressionGain,
    CaptureFixedPostGain,
    PlayoutVolumeChange,
    CustomRenderSetting,
    PlayoutAudioDeviceChange,
    CapturePostGain,
    CaptureOutputUsed
}
#pragma warning restore CS1591

/// <summary>
/// Represents a stream configuration for audio processing
/// </summary>
public class StreamConfig : IDisposable
{
    private IntPtr _nativeConfig;

    /// <summary>
    /// Creates a new stream configuration
    /// </summary>
    /// <param name="sampleRateHz">Sample rate in Hz</param>
    /// <param name="numChannels">Number of channels</param>
    public StreamConfig(int sampleRateHz, int numChannels)
    {
        _nativeConfig = NativeMethods.webrtc_apm_stream_config_create(sampleRateHz, (UIntPtr)numChannels);
        if (_nativeConfig == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create stream config");
    }

    /// <summary>
    /// Sample rate in Hz
    /// </summary>
    public int SampleRateHz => NativeMethods.webrtc_apm_stream_config_sample_rate_hz(_nativeConfig);

    /// <summary>
    /// Number of channels
    /// </summary>
    public int NumChannels => (int)NativeMethods.webrtc_apm_stream_config_num_channels(_nativeConfig);

    internal IntPtr NativePtr => _nativeConfig;

    #region IDisposable Support

    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (_nativeConfig != IntPtr.Zero)
            {
                NativeMethods.webrtc_apm_stream_config_destroy(_nativeConfig);
                _nativeConfig = IntPtr.Zero;
            }

            disposedValue = true;
        }
    }

    ~StreamConfig()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// Represents a processing configuration for audio processing
/// </summary>
public class ProcessingConfig : IDisposable
{
    private IntPtr _nativeConfig;

    /// <summary>
    /// Creates a new processing configuration
    /// </summary>
    public ProcessingConfig()
    {
        _nativeConfig = NativeMethods.webrtc_apm_processing_config_create();
        if (_nativeConfig == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create processing config");
    }

    /// <summary>
    /// Input stream configuration
    /// </summary>
    public StreamConfig InputStream
    {
        get
        {
            var ptr = NativeMethods.webrtc_apm_processing_config_input_stream(_nativeConfig);
            return new StreamConfig(
                NativeMethods.webrtc_apm_stream_config_sample_rate_hz(ptr),
                (int)NativeMethods.webrtc_apm_stream_config_num_channels(ptr));
        }
    }

    /// <summary>
    /// Output stream configuration
    /// </summary>
    public StreamConfig OutputStream
    {
        get
        {
            var ptr = NativeMethods.webrtc_apm_processing_config_output_stream(_nativeConfig);
            return new StreamConfig(
                NativeMethods.webrtc_apm_stream_config_sample_rate_hz(ptr),
                (int)NativeMethods.webrtc_apm_stream_config_num_channels(ptr));
        }
    }

    /// <summary>
    /// Reverse input stream configuration
    /// </summary>
    public StreamConfig ReverseInputStream
    {
        get
        {
            var ptr = NativeMethods.webrtc_apm_processing_config_reverse_input_stream(_nativeConfig);
            return new StreamConfig(
                NativeMethods.webrtc_apm_stream_config_sample_rate_hz(ptr),
                (int)NativeMethods.webrtc_apm_stream_config_num_channels(ptr));
        }
    }

    /// <summary>
    /// Reverse output stream configuration
    /// </summary>
    public StreamConfig ReverseOutputStream
    {
        get
        {
            var ptr = NativeMethods.webrtc_apm_processing_config_reverse_output_stream(_nativeConfig);
            return new StreamConfig(
                NativeMethods.webrtc_apm_stream_config_sample_rate_hz(ptr),
                (int)NativeMethods.webrtc_apm_stream_config_num_channels(ptr));
        }
    }

    internal IntPtr NativePtr => _nativeConfig;

    #region IDisposable Support

    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (_nativeConfig != IntPtr.Zero)
            {
                NativeMethods.webrtc_apm_processing_config_destroy(_nativeConfig);
                _nativeConfig = IntPtr.Zero;
            }

            disposedValue = true;
        }
    }

    ~ProcessingConfig()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// Represents an APM configuration
/// </summary>
public class ApmConfig : IDisposable
{
    private IntPtr _nativeConfig;

    /// <summary>
    /// Creates a new APM configuration
    /// </summary>
    public ApmConfig()
    {
        _nativeConfig = NativeMethods.webrtc_apm_config_create();
        if (_nativeConfig == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create APM config");
    }

    /// <summary>
    /// Configures the echo canceller
    /// </summary>
    /// <param name="enabled">Whether echo cancellation is enabled</param>
    /// <param name="mobileMode">Whether to use mobile mode</param>
    public void SetEchoCanceller(bool enabled, bool mobileMode)
    {
        NativeMethods.webrtc_apm_config_set_echo_canceller(_nativeConfig, enabled ? 1 : 0, mobileMode ? 1 : 0);
    }

    /// <summary>
    /// Configures noise suppression
    /// </summary>
    /// <param name="enabled">Whether noise suppression is enabled</param>
    /// <param name="level">Noise suppression level</param>
    public void SetNoiseSuppression(bool enabled, NoiseSuppressionLevel level)
    {
        NativeMethods.webrtc_apm_config_set_noise_suppression(_nativeConfig, enabled ? 1 : 0, level);
    }

    /// <summary>
    /// Configures gain controller 1
    /// </summary>
    /// <param name="enabled">Whether gain controller is enabled</param>
    /// <param name="mode">Gain control mode</param>
    /// <param name="targetLevelDbfs">Target level in dBFS</param>
    /// <param name="compressionGainDb">Compression gain in dB</param>
    /// <param name="enableLimiter">Whether to enable the limiter</param>
    public void SetGainController1(bool enabled, GainControlMode mode, int targetLevelDbfs, int compressionGainDb,
        bool enableLimiter)
    {
        NativeMethods.webrtc_apm_config_set_gain_controller1(
            _nativeConfig,
            enabled ? 1 : 0,
            mode,
            targetLevelDbfs,
            compressionGainDb,
            enableLimiter ? 1 : 0);
    }

    /// <summary>
    /// Configures gain controller 2
    /// </summary>
    /// <param name="enabled">Whether gain controller 2 is enabled</param>
    public void SetGainController2(bool enabled)
    {
        NativeMethods.webrtc_apm_config_set_gain_controller2(_nativeConfig, enabled ? 1 : 0);
    }

    /// <summary>
    /// Configures the high pass filter
    /// </summary>
    /// <param name="enabled">Whether the high pass filter is enabled</param>
    public void SetHighPassFilter(bool enabled)
    {
        NativeMethods.webrtc_apm_config_set_high_pass_filter(_nativeConfig, enabled ? 1 : 0);
    }

    /// <summary>
    /// Configures the pre-amplifier
    /// </summary>
    /// <param name="enabled">Whether the pre-amplifier is enabled</param>
    /// <param name="fixedGainFactor">Fixed gain factor</param>
    public void SetPreAmplifier(bool enabled, float fixedGainFactor)
    {
        NativeMethods.webrtc_apm_config_set_pre_amplifier(_nativeConfig, enabled ? 1 : 0, fixedGainFactor);
    }

    /// <summary>
    /// Configures the processing pipeline
    /// </summary>
    /// <param name="maxInternalRate">Maximum internal processing rate</param>
    /// <param name="multiChannelRender">Whether to enable multi-channel render</param>
    /// <param name="multiChannelCapture">Whether to enable multi-channel capture</param>
    /// <param name="downmixMethod">Downmix method</param>
    public void SetPipeline(int maxInternalRate, bool multiChannelRender, bool multiChannelCapture,
        DownmixMethod downmixMethod)
    {
        NativeMethods.webrtc_apm_config_set_pipeline(
            _nativeConfig,
            maxInternalRate,
            multiChannelRender ? 1 : 0,
            multiChannelCapture ? 1 : 0,
            downmixMethod);
    }

    internal IntPtr NativePtr => _nativeConfig;

    #region IDisposable Support

    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (_nativeConfig != IntPtr.Zero)
            {
                NativeMethods.webrtc_apm_config_destroy(_nativeConfig);
                _nativeConfig = IntPtr.Zero;
            }

            disposedValue = true;
        }
    }

    ~ApmConfig()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}

/// <summary>
/// Provides access to the WebRTC Audio Processing Module
/// </summary>
public class AudioProcessingModule : IDisposable
{
    public IntPtr NativePtr;

    /// <summary>
    /// Creates a new Audio Processing Module instance
    /// </summary>
    public AudioProcessingModule()
    {
        NativePtr = NativeMethods.webrtc_apm_create();
        if (NativePtr == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create APM instance");
    }

    /// <summary>
    /// Applies configuration to the APM
    /// </summary>
    /// <param name="config">Configuration to apply</param>
    /// <returns>Error code</returns>
    public ApmError ApplyConfig(ApmConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        return NativeMethods.webrtc_apm_apply_config(NativePtr, config.NativePtr);
    }

    /// <summary>
    /// Initializes the APM with default settings
    /// </summary>
    /// <returns>Error code</returns>
    public ApmError Initialize()
    {
        return NativeMethods.webrtc_apm_initialize(NativePtr);
    }
    
    /// <summary>
    /// Processes a stream of audio data
    /// </summary>
    /// <param name="src">Source audio data (array of channels)</param>
    /// <param name="inputConfig">Input stream configuration</param>
    /// <param name="outputConfig">Output stream configuration</param>
    /// <param name="dest">Destination audio data (array of channels)</param>
    /// <returns>Error code</returns>
    public ApmError ProcessStream(float[][] src, StreamConfig inputConfig, StreamConfig outputConfig, float[][] dest)
    {
        if (src == null || inputConfig == null || outputConfig == null || dest == null)
            throw new ArgumentNullException();

        // Convert float[][] to IntPtr[] for interop
        var srcPtrs = new IntPtr[src.Length];
        var destPtrs = new IntPtr[dest.Length];

        try
        {
            for (int i = 0; i < src.Length; i++)
            {
                srcPtrs[i] = Marshal.AllocHGlobal(src[i].Length * sizeof(float));
                Marshal.Copy(src[i], 0, srcPtrs[i], src[i].Length);
            }

            for (int i = 0; i < dest.Length; i++)
            {
                destPtrs[i] = Marshal.AllocHGlobal(dest[i].Length * sizeof(float));
            }

            // Pin the arrays
            GCHandle srcHandle = GCHandle.Alloc(srcPtrs, GCHandleType.Pinned);
            GCHandle destHandle = GCHandle.Alloc(destPtrs, GCHandleType.Pinned);

            try
            {
                var error = NativeMethods.webrtc_apm_process_stream(
                    NativePtr,
                    srcHandle.AddrOfPinnedObject(),
                    inputConfig.NativePtr,
                    outputConfig.NativePtr,
                    destHandle.AddrOfPinnedObject());

                // Copy results back
                for (int i = 0; i < dest.Length; i++)
                {
                    Marshal.Copy(destPtrs[i], dest[i], 0, dest[i].Length);
                }

                return error;
            }
            finally
            {
                srcHandle.Free();
                destHandle.Free();
            }
        }
        finally
        {
            // Free allocated memory
            for (int i = 0; i < srcPtrs.Length; i++)
            {
                if (srcPtrs[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(srcPtrs[i]);
            }

            for (int i = 0; i < destPtrs.Length; i++)
            {
                if (destPtrs[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(destPtrs[i]);
            }
        }
    }

    /// <summary>
    /// Processes a reverse stream of audio data
    /// </summary>
    /// <param name="src">Source audio data (array of channels)</param>
    /// <param name="inputConfig">Input stream configuration</param>
    /// <param name="outputConfig">Output stream configuration</param>
    /// <param name="dest">Destination audio data (array of channels)</param>
    /// <returns>Error code</returns>
    public ApmError ProcessReverseStream(float[][] src, StreamConfig inputConfig, StreamConfig outputConfig,
        float[][] dest)
    {
        if (src == null || inputConfig == null || outputConfig == null || dest == null)
            throw new ArgumentNullException();

        // Convert float[][] to IntPtr[] for interop
        var srcPtrs = new IntPtr[src.Length];
        var destPtrs = new IntPtr[dest.Length];

        try
        {
            for (int i = 0; i < src.Length; i++)
            {
                srcPtrs[i] = Marshal.AllocHGlobal(src[i].Length * sizeof(float));
                Marshal.Copy(src[i], 0, srcPtrs[i], src[i].Length);
            }

            for (int i = 0; i < dest.Length; i++)
            {
                destPtrs[i] = Marshal.AllocHGlobal(dest[i].Length * sizeof(float));
            }

            // Pin the arrays
            GCHandle srcHandle = GCHandle.Alloc(srcPtrs, GCHandleType.Pinned);
            GCHandle destHandle = GCHandle.Alloc(destPtrs, GCHandleType.Pinned);

            try
            {
                var error = NativeMethods.webrtc_apm_process_reverse_stream(
                    NativePtr,
                    srcHandle.AddrOfPinnedObject(),
                    inputConfig.NativePtr,
                    outputConfig.NativePtr,
                    destHandle.AddrOfPinnedObject());

                // Copy results back
                for (int i = 0; i < dest.Length; i++)
                {
                    Marshal.Copy(destPtrs[i], dest[i], 0, dest[i].Length);
                }

                return error;
            }
            finally
            {
                srcHandle.Free();
                destHandle.Free();
            }
        }
        finally
        {
            // Free allocated memory
            for (int i = 0; i < srcPtrs.Length; i++)
            {
                if (srcPtrs[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(srcPtrs[i]);
            }

            for (int i = 0; i < destPtrs.Length; i++)
            {
                if (destPtrs[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(destPtrs[i]);
            }
        }
    }

    /// <summary>
    /// Analyzes a reverse stream of audio data
    /// </summary>
    /// <param name="data">Audio data to analyze (array of channels)</param>
    /// <param name="reverseConfig">Reverse stream configuration</param>
    /// <returns>Error code</returns>
    public ApmError AnalyzeReverseStream(float[][] data, StreamConfig reverseConfig)
    {
        if (data == null || reverseConfig == null)
            throw new ArgumentNullException();

        // Convert float[][] to IntPtr[] for interop
        var dataPtrs = new IntPtr[data.Length];

        try
        {
            for (int i = 0; i < data.Length; i++)
            {
                dataPtrs[i] = Marshal.AllocHGlobal(data[i].Length * sizeof(float));
                Marshal.Copy(data[i], 0, dataPtrs[i], data[i].Length);
            }

            // Pin the array
            GCHandle dataHandle = GCHandle.Alloc(dataPtrs, GCHandleType.Pinned);

            try
            {
                return NativeMethods.webrtc_apm_analyze_reverse_stream(
                    NativePtr,
                    dataHandle.AddrOfPinnedObject(),
                    reverseConfig.NativePtr);
            }
            finally
            {
                dataHandle.Free();
            }
        }
        finally
        {
            // Free allocated memory
            for (int i = 0; i < dataPtrs.Length; i++)
            {
                if (dataPtrs[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(dataPtrs[i]);
            }
        }
    }

    /// <summary>
    /// Sets the analog level for the current stream
    /// </summary>
    /// <param name="level">Analog level (0-255)</param>
    public void SetStreamAnalogLevel(int level)
    {
        NativeMethods.webrtc_apm_set_stream_analog_level(NativePtr, level);
    }

    /// <summary>
    /// Gets the recommended analog level for the current stream
    /// </summary>
    /// <returns>Recommended analog level (0-255)</returns>
    public int GetRecommendedStreamAnalogLevel()
    {
        return NativeMethods.webrtc_apm_recommended_stream_analog_level(NativePtr);
    }

    /// <summary>
    /// Sets the delay in ms between ProcessReverseStream() and ProcessStream()
    /// </summary>
    /// <param name="delayMs">Delay in milliseconds</param>
    public void SetStreamDelayMs(int delayMs)
    {
        NativeMethods.webrtc_apm_set_stream_delay_ms(NativePtr, delayMs);
    }

    /// <summary>
    /// Gets the current stream delay
    /// </summary>
    /// <returns>Current delay in milliseconds</returns>
    public int GetStreamDelayMs()
    {
        return NativeMethods.webrtc_apm_stream_delay_ms(NativePtr);
    }

    /// <summary>
    /// Sets whether a key was pressed during this chunk of audio
    /// </summary>
    /// <param name="keyPressed">Whether a key was pressed</param>
    public void SetStreamKeyPressed(bool keyPressed)
    {
        NativeMethods.webrtc_apm_set_stream_key_pressed(NativePtr, keyPressed ? 1 : 0);
    }

    /// <summary>
    /// Sets whether the output will be muted or in some other way not used
    /// </summary>
    /// <param name="muted">Whether output will be muted</param>
    public void SetOutputWillBeMuted(bool muted)
    {
        NativeMethods.webrtc_apm_set_output_will_be_muted(NativePtr, muted ? 1 : 0);
    }

    /// <summary>
    /// Sets a runtime setting with a float value
    /// </summary>
    /// <param name="type">Type of setting</param>
    /// <param name="value">Float value</param>
    public void SetRuntimeSetting(RuntimeSettingType type, float value)
    {
        NativeMethods.webrtc_apm_set_runtime_setting_float(NativePtr, type, value);
    }

    /// <summary>
    /// Sets a runtime setting with an integer value
    /// </summary>
    /// <param name="type">Type of setting</param>
    /// <param name="value">Integer value</param>
    public void SetRuntimeSetting(RuntimeSettingType type, int value)
    {
        NativeMethods.webrtc_apm_set_runtime_setting_int(NativePtr, type, value);
    }

    /// <summary>
    /// Gets the current processing sample rate
    /// </summary>
    /// <returns>Sample rate in Hz</returns>
    public int GetProcSampleRateHz()
    {
        return NativeMethods.webrtc_apm_proc_sample_rate_hz(NativePtr);
    }

    /// <summary>
    /// Gets the current processing split sample rate
    /// </summary>
    /// <returns>Split sample rate in Hz</returns>
    public int GetProcSplitSampleRateHz()
    {
        return NativeMethods.webrtc_apm_proc_split_sample_rate_hz(NativePtr);
    }

    /// <summary>
    /// Gets the number of input channels
    /// </summary>
    /// <returns>Number of input channels</returns>
    public int GetNumInputChannels()
    {
        return (int)NativeMethods.webrtc_apm_num_input_channels(NativePtr);
    }

    /// <summary>
    /// Gets the number of processing channels
    /// </summary>
    /// <returns>Number of processing channels</returns>
    public int GetNumProcChannels()
    {
        return (int)NativeMethods.webrtc_apm_num_proc_channels(NativePtr);
    }

    /// <summary>
    /// Gets the number of output channels
    /// </summary>
    /// <returns>Number of output channels</returns>
    public int GetNumOutputChannels()
    {
        return (int)NativeMethods.webrtc_apm_num_output_channels(NativePtr);
    }

    /// <summary>
    /// Gets the number of reverse channels
    /// </summary>
    /// <returns>Number of reverse channels</returns>
    public int GetNumReverseChannels()
    {
        return (int)NativeMethods.webrtc_apm_num_reverse_channels(NativePtr);
    }

    /// <summary>
    /// Creates an AEC dump file
    /// </summary>
    /// <param name="fileName">Path to the dump file</param>
    /// <param name="maxLogSizeBytes">Maximum size of the log file in bytes (-1 for unlimited)</param>
    /// <returns>True if successful</returns>
    public bool CreateAecDump(string fileName, long maxLogSizeBytes)
    {
        return NativeMethods.webrtc_apm_create_aec_dump(NativePtr, fileName, maxLogSizeBytes) != 0;
    }

    /// <summary>
    /// Detaches the current AEC dump
    /// </summary>
    public void DetachAecDump()
    {
        NativeMethods.webrtc_apm_detach_aec_dump(NativePtr);
    }

    /// <summary>
    /// Calculates the frame size for a given sample rate
    /// </summary>
    /// <param name="sampleRateHz">Sample rate in Hz</param>
    /// <returns>Frame size in samples</returns>
    public static int GetFrameSize(int sampleRateHz)
    {
        return (int)NativeMethods.webrtc_apm_get_frame_size(sampleRateHz);
    }

    #region IDisposable Support

    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (NativePtr != IntPtr.Zero)
            {
                NativeMethods.webrtc_apm_destroy(NativePtr);
                NativePtr = IntPtr.Zero;
            }

            disposedValue = true;
        }
    }

    ~AudioProcessingModule()
    {
        Dispose(false);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}

public static class NativeMethods
{
    private const string LibraryName = "webrtc-apm";

    static NativeMethods()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, NativeLibraryResolver.Resolve);
    }

    private static class NativeLibraryResolver
    {
        public static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (NativeLibrary.TryLoad(LibraryName, out var library))
                return library;

            var libraryPath = GetLibraryPath(LibraryName);
            // Safeguard against dotnet cli working directory inconsistency
            if (!File.Exists(libraryPath))
                libraryPath = $"{Path.GetDirectoryName(assembly.Location)}/{libraryPath}";

            return NativeLibrary.Load(libraryPath);
        }

        private static string GetLibraryPath(string libraryName)
        {
            const string relativeBase = "runtimes";
            if (OperatingSystem.IsWindows())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X86 => $"{relativeBase}/win-x86/native/{libraryName}.dll",
                    Architecture.X64 => $"{relativeBase}/win-x64/native/{libraryName}.dll",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Windows architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsMacOS())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => $"{relativeBase}/osx-x64/native/lib{libraryName}.dylib",
                    Architecture.Arm64 => $"{relativeBase}/osx-arm64/native/lib{libraryName}.dylib",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported macOS architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsLinux())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => $"{relativeBase}/linux-x64/native/lib{libraryName}.so",
                    Architecture.Arm => $"{relativeBase}/linux-arm/native/lib{libraryName}.so",
                    Architecture.Arm64 => $"{relativeBase}/linux-arm64/native/lib{libraryName}.so",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Linux architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsAndroid())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => $"{relativeBase}/android-x64/native/lib{libraryName}.so",
                    Architecture.Arm64 => $"{relativeBase}/android-arm64/native/lib{libraryName}.so",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Android architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsIOS())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => $"{relativeBase}/ios-arm64/native/{libraryName}.framework/{libraryName}",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported iOS architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            throw new PlatformNotSupportedException(
                $"Unsupported operating system: {RuntimeInformation.OSDescription}");
        }
    }

#pragma warning disable CS1591
    [DllImport(LibraryName, EntryPoint = "webrtc_apm_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr webrtc_apm_create();

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_destroy(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_config_create", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr webrtc_apm_config_create();

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_config_destroy", CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_config_destroy(IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_config_set_echo_canceller",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_config_set_echo_canceller(IntPtr config, int enabled, int mobile_mode);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_config_set_noise_suppression",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_config_set_noise_suppression(IntPtr config, int enabled,
        NoiseSuppressionLevel level);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_config_set_gain_controller1",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_config_set_gain_controller1(IntPtr config, int enabled, GainControlMode mode,
        int target_level_dbfs, int compression_gain_db, int enable_limiter);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_config_set_gain_controller2",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_config_set_gain_controller2(IntPtr config, int enabled);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_config_set_high_pass_filter",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_config_set_high_pass_filter(IntPtr config, int enabled);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_config_set_pre_amplifier",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_config_set_pre_amplifier(IntPtr config, int enabled, float fixed_gain_factor);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_config_set_pipeline", CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_config_set_pipeline(IntPtr config, int max_internal_rate,
        int multi_channel_render, int multi_channel_capture, DownmixMethod downmix_method);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_apply_config", CallingConvention = CallingConvention.Cdecl)]
    public static extern ApmError webrtc_apm_apply_config(IntPtr apm, IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_stream_config_create",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr webrtc_apm_stream_config_create(int sample_rate_hz, UIntPtr num_channels);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_stream_config_destroy",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_stream_config_destroy(IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_stream_config_sample_rate_hz",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern int webrtc_apm_stream_config_sample_rate_hz(IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_stream_config_num_channels",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr webrtc_apm_stream_config_num_channels(IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_processing_config_create",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr webrtc_apm_processing_config_create();

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_processing_config_destroy",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_processing_config_destroy(IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_processing_config_input_stream",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr webrtc_apm_processing_config_input_stream(IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_processing_config_output_stream",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr webrtc_apm_processing_config_output_stream(IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_processing_config_reverse_input_stream",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr webrtc_apm_processing_config_reverse_input_stream(IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_processing_config_reverse_output_stream",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr webrtc_apm_processing_config_reverse_output_stream(IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_initialize", CallingConvention = CallingConvention.Cdecl)]
    public static extern ApmError webrtc_apm_initialize(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_initialize_with_config",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern ApmError webrtc_apm_initialize_with_config(IntPtr apm, IntPtr config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_process_stream", CallingConvention = CallingConvention.Cdecl)]
    public static extern ApmError webrtc_apm_process_stream(IntPtr apm, IntPtr src, IntPtr input_config,
        IntPtr output_config, IntPtr dest);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_process_reverse_stream",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern ApmError webrtc_apm_process_reverse_stream(IntPtr apm, IntPtr src, IntPtr input_config,
        IntPtr output_config, IntPtr dest);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_analyze_reverse_stream",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern ApmError webrtc_apm_analyze_reverse_stream(IntPtr apm, IntPtr data, IntPtr reverse_config);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_set_stream_analog_level",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_set_stream_analog_level(IntPtr apm, int level);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_recommended_stream_analog_level",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern int webrtc_apm_recommended_stream_analog_level(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_set_stream_delay_ms", CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_set_stream_delay_ms(IntPtr apm, int delay);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_stream_delay_ms", CallingConvention = CallingConvention.Cdecl)]
    public static extern int webrtc_apm_stream_delay_ms(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_set_stream_key_pressed",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_set_stream_key_pressed(IntPtr apm, int key_pressed);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_set_output_will_be_muted",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_set_output_will_be_muted(IntPtr apm, int muted);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_set_runtime_setting_float",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_set_runtime_setting_float(IntPtr apm, RuntimeSettingType type, float value);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_set_runtime_setting_int",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_set_runtime_setting_int(IntPtr apm, RuntimeSettingType type, int value);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_proc_sample_rate_hz", CallingConvention = CallingConvention.Cdecl)]
    public static extern int webrtc_apm_proc_sample_rate_hz(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_proc_split_sample_rate_hz",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern int webrtc_apm_proc_split_sample_rate_hz(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_num_input_channels", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr webrtc_apm_num_input_channels(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_num_proc_channels", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr webrtc_apm_num_proc_channels(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_num_output_channels", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr webrtc_apm_num_output_channels(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_num_reverse_channels",
        CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr webrtc_apm_num_reverse_channels(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_create_aec_dump", CallingConvention = CallingConvention.Cdecl)]
    public static extern int webrtc_apm_create_aec_dump(IntPtr apm, [MarshalAs(UnmanagedType.LPStr)] string file_name,
        long max_log_size_bytes);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_detach_aec_dump", CallingConvention = CallingConvention.Cdecl)]
    public static extern void webrtc_apm_detach_aec_dump(IntPtr apm);

    [DllImport(LibraryName, EntryPoint = "webrtc_apm_get_frame_size", CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr webrtc_apm_get_frame_size(int sample_rate_hz);
    
    #pragma warning restore CS1591
}