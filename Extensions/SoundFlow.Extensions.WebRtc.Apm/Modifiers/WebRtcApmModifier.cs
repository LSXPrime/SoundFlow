using System.Buffers;
using System.Runtime.InteropServices;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Enums;
using SoundFlow.Utils;

namespace SoundFlow.Extensions.WebRtc.Apm.Modifiers;

/// <summary>
/// A SoundModifier that applies WebRTC Audio Processing Module (APM) features
/// such as Echo Cancellation, Noise Suppression, and Automatic Gain Control
/// to the audio stream.
/// </summary>
public sealed class WebRtcApmModifier : SoundModifier, IDisposable
{
    private readonly AudioDevice _device;
    private AudioProcessingModule? _apm;
    private ApmConfig? _apmConfig;
    private readonly object _apmLock = new();

    private StreamConfig? _inputStreamConfig;
    private StreamConfig? _outputStreamConfig;
    private StreamConfig? _reverseInputStreamConfig;
    private StreamConfig? _reverseOutputStreamConfig;

    private readonly int _apmFrameSizePerChannel;
    private readonly int _numChannels;
    private readonly int _sampleRate;
    private const int BytesPerSample = sizeof(float);
    private readonly int _apmFrameSizeBytesPerChannel;

    private float[][]? _deinterleavedInputApmFrame;
    private float[][]? _deinterleavedOutputApmFrame;
    private float[][]? _deinterleavedFarendApmFrame;

    private nint[]? _inputChannelPtrs;
    private nint[]? _outputChannelPtrs;
    private nint _inputChannelArrayPtr = nint.Zero;
    private nint _outputChannelArrayPtr = nint.Zero;
    private GCHandle _inputChannelArrayHandle;
    private GCHandle _outputChannelArrayHandle;

    private nint[]? _farendChannelPtrs;
    private nint _farendChannelArrayPtr = nint.Zero;
    private GCHandle _farendChannelArrayHandle;
    private nint[]? _dummyReverseOutputChannelPtrs; // For AEC farend processing
    private nint _dummyReverseOutputChannelArrayPtr = nint.Zero;
    private GCHandle _dummyReverseOutputChannelArrayHandle;

    private readonly Queue<float> _inputRingBuffer = new();
    private readonly Queue<float> _outputRingBuffer = new();
    private readonly Queue<float> _farendInputRingBuffer = new();

    private bool _isApmSuccessfullyInitialized;
    private bool _isDisposed;

    /// <summary>
    /// Gets or sets the name of the audio modifier.
    /// </summary>
    public override string Name { get; set; } = "WebRTC APM Suite Modifier";

    #region Nested Settings Classes

    /// <summary>
    /// Settings for the Echo Cancellation feature.
    /// </summary>
    public class EchoCancellationSettings
    {
        private readonly WebRtcApmModifier _parent;
        private bool _enabled;
        private bool _mobileMode;
        private int _latencyMs;

        /// <summary>
        /// Gets or sets a value indicating whether Echo Cancellation is enabled.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether mobile mode is enabled for Echo Cancellation.
        /// Mobile mode often tolerates higher latency but might be less aggressive.
        /// </summary>
        public bool MobileMode
        {
            get => _mobileMode;
            set
            {
                if (_mobileMode == value) return;
                _mobileMode = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets the estimated audio processing latency in milliseconds.
        /// Used by AEC to align near-end and far-end signals. Must be non-negative.
        /// </summary>
        public int LatencyMs
        {
            get => _latencyMs;
            set
            {
                if (_latencyMs == value) return;
                _latencyMs = Math.Max(0, value);
                _parent.UpdateApmStreamDelay();
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EchoCancellationSettings"/> class.
        /// </summary>
        /// <param name="parent">The parent modifier instance.</param>
        /// <param name="enabled">Initial state of Enabled property.</param>
        /// <param name="mobileMode">Initial state of MobileMode property.</param>
        /// <param name="latencyMs">Initial state of LatencyMs property.</param>
        internal EchoCancellationSettings(WebRtcApmModifier parent, bool enabled, bool mobileMode, int latencyMs)
        {
            _parent = parent;
            _enabled = enabled;
            _mobileMode = mobileMode;
            _latencyMs = latencyMs;
        }
    }

    /// <summary>
    /// Settings for the Noise Suppression feature.
    /// </summary>
    public class NoiseSuppressionSettings
    {
        private readonly WebRtcApmModifier _parent;
        private bool _enabled;
        private NoiseSuppressionLevel _level;

        /// <summary>
        /// Gets or sets a value indicating whether Noise Suppression is enabled.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets the level of noise suppression to apply.
        /// </summary>
        public NoiseSuppressionLevel Level
        {
            get => _level;
            set
            {
                if (_level == value) return;
                _level = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NoiseSuppressionSettings"/> class.
        /// </summary>
        /// <param name="parent">The parent modifier instance.</param>
        /// <param name="enabled">Initial state of Enabled property.</param>
        /// <param name="level">Initial state of Level property.</param>
        internal NoiseSuppressionSettings(WebRtcApmModifier parent, bool enabled, NoiseSuppressionLevel level)
        {
            _parent = parent;
            _enabled = enabled;
            _level = level;
        }
    }

    /// <summary>
    /// Settings for the Automatic Gain Control feature.
    /// Note: WebRTC has two AGC implementations (AGC1 and AGC2). AGC2 is newer.
    /// Settings like Mode, TargetLevelDbfs, etc., primarily apply to AGC1.
    /// </summary>
    public class AutomaticGainControlSettings
    {
        private readonly WebRtcApmModifier _parent;
        private bool _agc1Enabled;
        private GainControlMode _mode;
        private int _targetLevelDbfs;
        private int _compressionGainDb;
        private bool _limiterEnabled;
        private bool _agc2Enabled;

        /// <summary>
        /// Gets or sets a value indicating whether AGC1 (legacy) is enabled.
        /// </summary>
        public bool Agc1Enabled
        {
            get => _agc1Enabled;
            set
            {
                if (_agc1Enabled == value) return;
                _agc1Enabled = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets the AGC mode (primarily affects AGC1).
        /// </summary>
        public GainControlMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets the target output level in dBFS for Adaptive Digital AGC (AGC1).
        /// Clamped between -31 and 0.
        /// </summary>
        public int TargetLevelDbfs
        {
            get => _targetLevelDbfs;
            set
            {
                var v = Math.Clamp(value, -31, 0);
                if (_targetLevelDbfs == v) return;
                _targetLevelDbfs = v;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets the compression gain in dB for Fixed Digital AGC (AGC1).
        /// Clamped between 0 and 90.
        /// </summary>
        public int CompressionGainDb
        {
            get => _compressionGainDb;
            set
            {
                var v = Math.Clamp(value, 0, 90);
                if (_compressionGainDb == v) return;
                _compressionGainDb = v;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the AGC limiter is enabled (AGC1).
        /// </summary>
        public bool LimiterEnabled
        {
            get => _limiterEnabled;
            set
            {
                if (_limiterEnabled == value) return;
                _limiterEnabled = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether AGC2 (newer) is enabled.
        /// Note: AGC1 and AGC2 can potentially run simultaneously, but might be mutually exclusive depending on WebRTC version/config.
        /// Check WebRTC documentation for behavior when both are enabled.
        /// </summary>
        public bool Agc2Enabled
        {
            get => _agc2Enabled;
            set
            {
                if (_agc2Enabled == value) return;
                _agc2Enabled = value;
                _parent.UpdateApmConfiguration();
            }
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="AutomaticGainControlSettings"/> class.
        /// </summary>
        /// <param name="parent">The parent modifier instance.</param>
        /// <param name="agc1Enabled">Initial state of Agc1Enabled property.</param>
        /// <param name="mode">Initial state of Mode property.</param>
        /// <param name="targetLevelDbfs">Initial state of TargetLevelDbfs property.</param>
        /// <param name="compressionGainDb">Initial state of CompressionGainDb property.</param>
        /// <param name="limiterEnabled">Initial state of LimiterEnabled property.</param>
        /// <param name="agc2Enabled">Initial state of Agc2Enabled property.</param>
        internal AutomaticGainControlSettings(WebRtcApmModifier parent, bool agc1Enabled, GainControlMode mode,
            int targetLevelDbfs, int compressionGainDb, bool limiterEnabled, bool agc2Enabled)
        {
            _parent = parent;
            _agc1Enabled = agc1Enabled;
            _mode = mode;
            _targetLevelDbfs = targetLevelDbfs;
            _compressionGainDb = compressionGainDb;
            _limiterEnabled = limiterEnabled;
            _agc2Enabled = agc2Enabled;
        }
    }

    /// <summary>
    /// Settings for configuring the processing pipeline within WebRTC APM,
    /// specifically how multichannel audio is handled and downmixed.
    /// </summary>
    public class ProcessingPipelineSettings
    {
        private readonly WebRtcApmModifier _parent;
        private bool _useMultichannelCapture;
        private bool _useMultichannelRender;
        private DownmixMethod _downmixMethod;

        /// <summary>
        /// Gets or sets a value indicating whether multichannel processing is enabled for the capture (near-end) stream.
        /// If false, input channels might be downmixed before processing.
        /// </summary>
        public bool UseMultichannelCapture
        {
            get => _useMultichannelCapture;
            set
            {
                if (_useMultichannelCapture == value) return;
                _useMultichannelCapture = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether multichannel processing is enabled for the render (far-end) stream.
        /// If false, input channels might be downmixed before processing.
        /// </summary>
        public bool UseMultichannelRender
        {
            get => _useMultichannelRender;
            set
            {
                if (_useMultichannelRender == value) return;
                _useMultichannelRender = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Gets or sets the method used for downmixing channels if multichannel processing is disabled for a stream.
        /// </summary>
        public DownmixMethod DownmixMethod
        {
            get => _downmixMethod;
            set
            {
                if (_downmixMethod == value) return;
                _downmixMethod = value;
                _parent.UpdateApmConfiguration();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ProcessingPipelineSettings"/> class.
        /// </summary>
        /// <param name="parent">The parent modifier instance.</param>
        /// <param name="useMultichannelCapture">Initial state for UseMultichannelCapture property.</param>
        /// <param name="useMultichannelRender">Initial state for UseMultichannelRender property.</param>
        /// <param name="downmixMethod">Initial state for DownmixMethod property.</param>
        internal ProcessingPipelineSettings(WebRtcApmModifier parent, bool useMultichannelCapture,
            bool useMultichannelRender, DownmixMethod downmixMethod)
        {
            _parent = parent;
            _useMultichannelCapture = useMultichannelCapture;
            _useMultichannelRender = useMultichannelRender;
            _downmixMethod = downmixMethod;
        }
    }

    #endregion

    #region Public Configuration Properties

    /// <summary>
    /// Gets the settings object for Echo Cancellation.
    /// </summary>
    public EchoCancellationSettings EchoCancellation { get; }

    /// <summary>
    /// Gets the settings object for Noise Suppression.
    /// </summary>
    public NoiseSuppressionSettings NoiseSuppression { get; }

    /// <summary>
    /// Gets the settings object for Automatic Gain Control.
    /// </summary>
    public AutomaticGainControlSettings AutomaticGainControl { get; }

    /// <summary>
    /// Gets the settings object for configuring the processing pipeline (multichannel, downmixing).
    /// </summary>
    public ProcessingPipelineSettings ProcessingPipeline { get; }

    private bool _highPassFilterEnabled;

    /// <summary>
    /// Gets or sets a value indicating whether the High Pass Filter is enabled.
    /// This filter removes frequencies below 80 Hz.
    /// </summary>
    public bool HighPassFilterEnabled
    {
        get => _highPassFilterEnabled;
        set
        {
            if (_highPassFilterEnabled == value) return;
            _highPassFilterEnabled = value;
            UpdateApmConfiguration();
        }
    }

    private bool _preAmplifierEnabled;

    /// <summary>
    /// Gets or sets a value indicating whether the Pre-Amplifier is enabled.
    /// Applies a gain factor before other processing steps.
    /// </summary>
    public bool PreAmplifierEnabled
    {
        get => _preAmplifierEnabled;
        set
        {
            if (_preAmplifierEnabled == value) return;
            _preAmplifierEnabled = value;
            UpdateApmConfiguration();
        }
    }

    private float _preAmplifierGainFactor = 1.0f;

    /// <summary>
    /// Gets or sets the gain factor applied by the Pre-Amplifier.
    /// Only active if <see cref="PreAmplifierEnabled"/> is true.
    /// </summary>
    public float PreAmplifierGainFactor
    {
        get => _preAmplifierGainFactor;
        set
        {
            if (Math.Abs(_preAmplifierGainFactor - value) < 0.001f) return;
            _preAmplifierGainFactor = value;
            UpdateApmConfiguration();
        }
    }


    /// <summary>
    /// Gets or sets a post-processing gain factor applied after WebRTC APM processing.
    /// </summary>
    public float PostProcessGain { get; set; } = 1f;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="WebRtcApmModifier"/> class with specified settings.
    /// </summary>
    /// <param name="device">The capture device to source audio from.</param>
    /// <param name="aecEnabled">Initial state for Echo Cancellation Enabled.</param>
    /// <param name="aecMobileMode">Initial state for Echo Cancellation Mobile Mode.</param>
    /// <param name="aecLatencyMs">Initial state for Echo Cancellation Latency in ms.</param>
    /// <param name="nsEnabled">Initial state for Noise Suppression Enabled.</param>
    /// <param name="nsLevel">Initial state for Noise Suppression Level.</param>
    /// <param name="agc1Enabled">Initial state for AGC1 Enabled.</param>
    /// <param name="agcMode">Initial state for AGC Mode (primarily AGC1).</param>
    /// <param name="agcTargetLevel">Initial state for AGC Target Level dBFS (AGC1 Adaptive Digital).</param>
    /// <param name="agcCompressionGain">Initial state for AGC Compression Gain dB (AGC1 Fixed Digital).</param>
    /// <param name="agcLimiter">Initial state for AGC Limiter Enabled (AGC1).</param>
    /// <param name="agc2Enabled">Initial state for AGC2 Enabled.</param>
    /// <param name="hpfEnabled">Initial state for High Pass Filter Enabled.</param>
    /// <param name="preAmpEnabled">Initial state for Pre-Amplifier Enabled.</param>
    /// <param name="preAmpGain">Initial state for Pre-Amplifier Gain Factor.</param>
    /// <param name="useMultichannelCapture">Initial state for ProcessingPipeline.UseMultichannelCapture.</param>
    /// <param name="useMultichannelRender">Initial state for ProcessingPipeline.UseMultichannelRender.</param>
    /// <param name="downmixMethod">Initial state for ProcessingPipeline.DownmixMethod.</param>
    /// <exception cref="ArgumentException">Thrown if the audio sample rate is not supported by WebRTC APM (8000, 16000, 32000, or 48000 Hz).</exception>
    public WebRtcApmModifier(
        AudioDevice device,
        bool aecEnabled = false, bool aecMobileMode = false, int aecLatencyMs = 40,
        bool nsEnabled = false, NoiseSuppressionLevel nsLevel = NoiseSuppressionLevel.High,
        bool agc1Enabled = false, GainControlMode agcMode = GainControlMode.AdaptiveDigital,
        int agcTargetLevel = -3, int agcCompressionGain = 9, bool agcLimiter = true,
        bool agc2Enabled = false,
        bool hpfEnabled = false,
        bool preAmpEnabled = false, float preAmpGain = 1.0f,
        bool? useMultichannelCapture = null, bool? useMultichannelRender = null,
        DownmixMethod downmixMethod = DownmixMethod.AverageChannels)
    {
        _device = device;
        _sampleRate = _device.Format.SampleRate;
        _numChannels = _device.Format.Channels;

        if (_sampleRate != 8000 && _sampleRate != 16000 && _sampleRate != 32000 && _sampleRate != 48000)
            throw new ArgumentException(
                $"Unsupported sample rate for WebRTC Audio Processing Module: {_sampleRate} Hz. Must be 8k, 16k, 32k, or 48k.");

        _apmFrameSizePerChannel = AudioProcessingModule.GetFrameSize(_sampleRate);
        _apmFrameSizeBytesPerChannel = _apmFrameSizePerChannel * BytesPerSample;

        if (_apmFrameSizePerChannel == 0 || _numChannels <= 0)
        {
            Console.Error.WriteLine(
                $"WebRTC APM Modifier: Invalid frame size or channel count ({_apmFrameSizePerChannel}, {_numChannels}). Disabling.");
            Enabled = false;
            // Initialize readonly properties to avoid null issues if accessed before full init
            EchoCancellation = new EchoCancellationSettings(this, false, false, 0);
            NoiseSuppression = new NoiseSuppressionSettings(this, false, NoiseSuppressionLevel.Low);
            AutomaticGainControl =
                new AutomaticGainControlSettings(this, false, GainControlMode.FixedDigital, 0, 0, false, false);
            ProcessingPipeline = new ProcessingPipelineSettings(this, false, false, DownmixMethod.AverageChannels);
            return;
        }

        // Determine default multichannel settings based on channel count, unless explicitly overridden
        var defaultUseMultiChannel = _numChannels > 1;
        var initialUseMultichannelCapture = useMultichannelCapture ?? defaultUseMultiChannel;
        var initialUseMultichannelRender = useMultichannelRender ?? defaultUseMultiChannel;


        EchoCancellation = new EchoCancellationSettings(this, aecEnabled, aecMobileMode, aecLatencyMs);
        NoiseSuppression = new NoiseSuppressionSettings(this, nsEnabled, nsLevel);
        AutomaticGainControl = new AutomaticGainControlSettings(this, agc1Enabled, agcMode, agcTargetLevel,
            agcCompressionGain, agcLimiter, agc2Enabled);
        ProcessingPipeline = new ProcessingPipelineSettings(this, initialUseMultichannelCapture,
            initialUseMultichannelRender, downmixMethod); // Initialize new property
        _highPassFilterEnabled = hpfEnabled;
        _preAmplifierEnabled = preAmpEnabled;
        _preAmplifierGainFactor = preAmpGain;


        InitializeApmAndFeatures();
    }

    private void InitializeApmAndFeatures()
    {
        if (_isApmSuccessfullyInitialized) return;
        lock (_apmLock)
        {
            if (_isApmSuccessfullyInitialized) return;
            try
            {
                _apm = new AudioProcessingModule();
                _apmConfig = new ApmConfig();

                ApplyAllSettingsToConfig(_apmConfig); // Initial full configuration

                var applyError = _apm.ApplyConfig(_apmConfig);
                if (applyError != ApmError.NoError)
                    throw new InvalidOperationException($"Failed to apply APM config: {applyError}");

                _inputStreamConfig = new StreamConfig(_sampleRate, _numChannels);
                _outputStreamConfig = new StreamConfig(_sampleRate, _numChannels);
                _reverseInputStreamConfig = new StreamConfig(_sampleRate, _numChannels);
                _reverseOutputStreamConfig = new StreamConfig(_sampleRate, _numChannels);

                var initError = _apm.Initialize();
                if (initError != ApmError.NoError)
                    throw new InvalidOperationException($"Failed to initialize APM: {initError}");

                _deinterleavedInputApmFrame = new float[_numChannels][];
                _deinterleavedOutputApmFrame = new float[_numChannels][];
                _deinterleavedFarendApmFrame = new float[_numChannels][];

                _inputChannelPtrs = new nint[_numChannels];
                _outputChannelPtrs = new nint[_numChannels];
                _farendChannelPtrs = new nint[_numChannels];
                _dummyReverseOutputChannelPtrs = new nint[_numChannels];

                for (var i = 0; i < _numChannels; i++)
                {
                    _deinterleavedInputApmFrame[i] = new float[_apmFrameSizePerChannel];
                    _deinterleavedOutputApmFrame[i] = new float[_apmFrameSizePerChannel];
                    _deinterleavedFarendApmFrame[i] = new float[_apmFrameSizePerChannel];
                    _inputChannelPtrs[i] = Marshal.AllocHGlobal(_apmFrameSizeBytesPerChannel);
                    _outputChannelPtrs[i] = Marshal.AllocHGlobal(_apmFrameSizeBytesPerChannel);
                    _farendChannelPtrs[i] = Marshal.AllocHGlobal(_apmFrameSizeBytesPerChannel);
                    _dummyReverseOutputChannelPtrs[i] = Marshal.AllocHGlobal(_apmFrameSizeBytesPerChannel);
                }

                _inputChannelArrayHandle = GCHandle.Alloc(_inputChannelPtrs, GCHandleType.Pinned);
                _inputChannelArrayPtr = _inputChannelArrayHandle.AddrOfPinnedObject();
                _outputChannelArrayHandle = GCHandle.Alloc(_outputChannelPtrs, GCHandleType.Pinned);
                _outputChannelArrayPtr = _outputChannelArrayHandle.AddrOfPinnedObject();
                _farendChannelArrayHandle = GCHandle.Alloc(_farendChannelPtrs, GCHandleType.Pinned);
                _farendChannelArrayPtr = _farendChannelArrayHandle.AddrOfPinnedObject();
                _dummyReverseOutputChannelArrayHandle =
                    GCHandle.Alloc(_dummyReverseOutputChannelPtrs, GCHandleType.Pinned);
                _dummyReverseOutputChannelArrayPtr = _dummyReverseOutputChannelArrayHandle.AddrOfPinnedObject();

                if (EchoCancellation.Enabled)
                    switch (_device)
                    {
                        case AudioCaptureDevice audioCaptureDevice:
                            audioCaptureDevice.OnAudioProcessed += HandleAudioEngineProcessedForAec;
                            break;
                        case FullDuplexDevice fullDuplexDevice:
                            fullDuplexDevice.OnAudioProcessed += HandleAudioEngineProcessedForAec;
                            break;
                    }

                UpdateApmStreamDelay();

                _isApmSuccessfullyInitialized = true;
            }
            catch (Exception ex)
            {
                Log.Error($"[WebRtcApmModifier] Init Exception: {ex.Message}");
                Enabled = false;
                DisposeApmNativeResources();
                throw;
            }
        }
    }

    private void ApplyAllSettingsToConfig(ApmConfig config)
    {
        config.SetEchoCanceller(EchoCancellation.Enabled, EchoCancellation.MobileMode);
        config.SetNoiseSuppression(NoiseSuppression.Enabled, NoiseSuppression.Level);
        config.SetGainController1(AutomaticGainControl.Agc1Enabled, AutomaticGainControl.Mode,
            AutomaticGainControl.TargetLevelDbfs, AutomaticGainControl.CompressionGainDb,
            AutomaticGainControl.LimiterEnabled);
        config.SetGainController2(AutomaticGainControl.Agc2Enabled);
        config.SetHighPassFilter(HighPassFilterEnabled);
        config.SetPreAmplifier(PreAmplifierEnabled, PreAmplifierGainFactor);
        config.SetPipeline(_sampleRate, ProcessingPipeline.UseMultichannelCapture,
            ProcessingPipeline.UseMultichannelRender, ProcessingPipeline.DownmixMethod);
    }

    private void UpdateApmConfiguration()
    {
        if (!Enabled || !_isApmSuccessfullyInitialized || _apm == null || _apmConfig == null) return;
        lock (_apmLock)
        {
            if (!_isApmSuccessfullyInitialized) return;
            ApplyAllSettingsToConfig(_apmConfig);
            var error = _apm.ApplyConfig(_apmConfig);
            if (error != ApmError.NoError)
                Console.Error.WriteLine($"WebRTC APM Modifier: Failed to re-apply APM config: {error}.");
        }
    }

    private void UpdateApmStreamDelay()
    {
        if (!Enabled || !_isApmSuccessfullyInitialized || _apm == null) return;
        lock (_apmLock)
        {
            if (!_isApmSuccessfullyInitialized || _apm == null) return;
            _apm.SetStreamDelayMs(EchoCancellation.LatencyMs);
        }
    }


    /// <summary>
    /// Processes the given audio buffer (near-end stream) using the WebRTC APM.
    /// Input samples are queued and processed in frames corresponding to the APM's internal frame size.
    /// Output samples are dequeued and written back to the buffer.
    /// </summary>
    /// <param name="buffer">The audio buffer to process (interleaved float samples).</param>
    /// <param name="channels">The number of channels in the audio buffer.</param>
    public override void Process(Span<float> buffer, int channels) // Near-end processing
    {
        if (!Enabled || !_isApmSuccessfullyInitialized || _apm == null || _apmConfig == null ||
            _inputStreamConfig == null || _outputStreamConfig == null || _deinterleavedInputApmFrame == null ||
            _deinterleavedOutputApmFrame == null || _inputChannelArrayPtr == nint.Zero ||
            _outputChannelArrayPtr == nint.Zero || buffer.Length == 0)
            return;

        foreach (var t in buffer)
            _inputRingBuffer.Enqueue(t);

        var totalSamplesInApmFrame = _apmFrameSizePerChannel * _numChannels;
        var processedAnyFrames = false;

        while (_inputRingBuffer.Count >= totalSamplesInApmFrame)
        {
            processedAnyFrames = true;
            var currentApmInterleavedInputFrame = ArrayPool<float>.Shared.Rent(totalSamplesInApmFrame);
            try
            {
                for (var i = 0; i < totalSamplesInApmFrame; i++)
                    if (!_inputRingBuffer.TryDequeue(out currentApmInterleavedInputFrame[i]))
                        break;

                Deinterleave(currentApmInterleavedInputFrame.AsSpan(0, totalSamplesInApmFrame),
                    _numChannels, _apmFrameSizePerChannel, _deinterleavedInputApmFrame);

                for (var ch = 0; ch < _numChannels; ch++)
                    Marshal.Copy(_deinterleavedInputApmFrame[ch], 0, _inputChannelPtrs![ch], _apmFrameSizePerChannel);

                ApmError error;
                lock (_apmLock)
                {
                    if (!_isApmSuccessfullyInitialized || _apm == null) error = ApmError.UnspecifiedError;
                    else
                        error = NativeMethods.ProcessStream(
                            _apm.NativePtr, _inputChannelArrayPtr,
                            _inputStreamConfig.NativePtr, _outputStreamConfig.NativePtr,
                            _outputChannelArrayPtr);
                }

                var resultBufferToInterleave = _deinterleavedInputApmFrame;
                if (error == ApmError.NoError)
                {
                    for (var ch = 0; ch < _numChannels; ch++)
                        Marshal.Copy(_outputChannelPtrs![ch], _deinterleavedOutputApmFrame![ch], 0,
                            _apmFrameSizePerChannel);
                    resultBufferToInterleave = _deinterleavedOutputApmFrame;
                }
                else
                {
                    Console.Error.WriteLine($"WebRTC APM: Error processing stream: {error}. Passing through.");
                }

                Interleave(resultBufferToInterleave, _numChannels, _apmFrameSizePerChannel,
                    currentApmInterleavedInputFrame.AsSpan(0, totalSamplesInApmFrame));

                for (var i = 0; i < totalSamplesInApmFrame; i++)
                    _outputRingBuffer.Enqueue(currentApmInterleavedInputFrame[i]);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(currentApmInterleavedInputFrame);
            }
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            if (_outputRingBuffer.TryDequeue(out var sample)) buffer[i] = sample * PostProcessGain;
            else
            {
                if (processedAnyFrames) buffer[i..].Clear();
                break;
            }
        }
    }

    private void HandleAudioEngineProcessedForAec(Span<float> samples, Capability capability) // Far-end processing
    {
        if (capability != Capability.Playback || !Enabled || !_isApmSuccessfullyInitialized || _apm == null ||
            !EchoCancellation.Enabled || // Only process if AEC itself is enabled
            _deinterleavedFarendApmFrame == null || _reverseInputStreamConfig == null ||
            _reverseOutputStreamConfig == null || _farendChannelArrayPtr == nint.Zero ||
            _dummyReverseOutputChannelArrayPtr == nint.Zero || samples.Length == 0)
            return;

        foreach (var sample in samples) _farendInputRingBuffer.Enqueue(sample);

        var totalSamplesInApmFrame = _apmFrameSizePerChannel * _numChannels;

        while (_farendInputRingBuffer.Count >= totalSamplesInApmFrame)
        {
            var currentApmInterleavedFarendFrame = ArrayPool<float>.Shared.Rent(totalSamplesInApmFrame);
            try
            {
                for (var i = 0; i < totalSamplesInApmFrame; i++)
                    if (!_farendInputRingBuffer.TryDequeue(out currentApmInterleavedFarendFrame[i]))
                        break;

                Deinterleave(currentApmInterleavedFarendFrame.AsSpan(0, totalSamplesInApmFrame),
                    _numChannels, _apmFrameSizePerChannel, _deinterleavedFarendApmFrame);

                for (var ch = 0; ch < _numChannels; ch++)
                    Marshal.Copy(_deinterleavedFarendApmFrame[ch], 0, _farendChannelPtrs![ch], _apmFrameSizePerChannel);

                ApmError error;
                lock (_apmLock)
                {
                    if (!_isApmSuccessfullyInitialized || _apm == null) error = ApmError.UnspecifiedError;
                    else
                        error = NativeMethods.ProcessReverseStream(
                            _apm.NativePtr, _farendChannelArrayPtr,
                            _reverseInputStreamConfig.NativePtr, _reverseOutputStreamConfig.NativePtr,
                            _dummyReverseOutputChannelArrayPtr);
                }

                if (error != ApmError.NoError)
                    Console.Error.WriteLine($"WebRTC APM: Error processing reverse stream: {error}.");
            }
            finally
            {
                ArrayPool<float>.Shared.Return(currentApmInterleavedFarendFrame);
            }
        }
    }

    /// <summary>
    /// This method is not supported by the WebRtcApmModifier as it processes audio in frames, not sample-by-sample.
    /// </summary>
    /// <param name="sample">The input sample.</param>
    /// <param name="channel">The channel index of the sample.</param>
    /// <returns>The processed sample (this method always throws).</returns>
    /// <exception cref="NotSupportedException">Always thrown by this method.</exception>
    public override float ProcessSample(float sample, int channel) =>
        throw new NotSupportedException("WebRtcApmModifier processes audio in frames.");

    private static void Deinterleave(ReadOnlySpan<float> interleaved, int numChannels, int frameSizePerChannel,
        float[][]? deinterleavedTarget)
    {
        if (deinterleavedTarget == null) return;
        for (var ch = 0; ch < numChannels; ch++)
        {
            if (deinterleavedTarget[ch] == null || deinterleavedTarget[ch].Length != frameSizePerChannel)
                deinterleavedTarget[ch] = new float[frameSizePerChannel];
            for (var i = 0; i < frameSizePerChannel; i++)
            {
                var idx = i * numChannels + ch;
                deinterleavedTarget[ch][i] = idx < interleaved.Length ? interleaved[idx] : 0f;
            }
        }
    }

    private static void Interleave(float[][] deinterleaved, int numChannels, int frameSizePerChannel,
        Span<float> interleavedTarget)
    {
        for (var ch = 0; ch < numChannels; ch++)
        {
            if (deinterleaved?[ch] == null || deinterleaved[ch].Length < frameSizePerChannel) continue;
            for (var i = 0; i < frameSizePerChannel; i++)
            {
                var idx = i * numChannels + ch;
                if (idx < interleavedTarget.Length) interleavedTarget[idx] = deinterleaved[ch][i];
            }
        }
    }

    private void DisposeApmNativeResources()
    {
        lock (_apmLock)
        {
            // Free GCHandles first
            if (_inputChannelArrayHandle.IsAllocated) _inputChannelArrayHandle.Free();
            if (_outputChannelArrayHandle.IsAllocated) _outputChannelArrayHandle.Free();
            if (_farendChannelArrayHandle.IsAllocated) _farendChannelArrayHandle.Free();
            if (_dummyReverseOutputChannelArrayHandle.IsAllocated) _dummyReverseOutputChannelArrayHandle.Free();
            _inputChannelArrayPtr = _outputChannelArrayPtr =
                _farendChannelArrayPtr = _dummyReverseOutputChannelArrayPtr = nint.Zero;

            // Free HGlobal memory
            Action<nint[]?> freePtrArray = arr =>
            {
                if (arr != null)
                    foreach (var ptr in arr)
                        if (ptr != nint.Zero)
                            Marshal.FreeHGlobal(ptr);
            };
            freePtrArray(_inputChannelPtrs);
            _inputChannelPtrs = null;
            freePtrArray(_outputChannelPtrs);
            _outputChannelPtrs = null;
            freePtrArray(_farendChannelPtrs);
            _farendChannelPtrs = null;
            freePtrArray(_dummyReverseOutputChannelPtrs);
            _dummyReverseOutputChannelPtrs = null;

            // Dispose managed APM wrappers
            _apm?.Dispose();
            _apm = null;
            _apmConfig?.Dispose();
            _apmConfig = null;
            _inputStreamConfig?.Dispose();
            _inputStreamConfig = null;
            _outputStreamConfig?.Dispose();
            _outputStreamConfig = null;
            _reverseInputStreamConfig?.Dispose();
            _reverseInputStreamConfig = null;
            _reverseOutputStreamConfig?.Dispose();
            _reverseOutputStreamConfig = null;

            _isApmSuccessfullyInitialized = false;
        }
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="WebRtcApmModifier"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    private void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing && _isApmSuccessfullyInitialized && EchoCancellation.Enabled)
                switch (_device)
                {
                    case AudioCaptureDevice audioCaptureDevice:
                        audioCaptureDevice.OnAudioProcessed -= HandleAudioEngineProcessedForAec;
                        break;
                    case FullDuplexDevice fullDuplexDevice:
                        fullDuplexDevice.OnAudioProcessed -= HandleAudioEngineProcessedForAec;
                        break;
                }

            // Free unmanaged resources (native handles, pointers)
            DisposeApmNativeResources();

            // Clear buffers
            _inputRingBuffer.Clear();
            _outputRingBuffer.Clear();
            _farendInputRingBuffer.Clear();

            // Set large objects to null for GC
            _deinterleavedInputApmFrame = null;
            _deinterleavedOutputApmFrame = null;
            _deinterleavedFarendApmFrame = null;


            _isDisposed = true;
        }
    }

    /// <summary>
    /// Finalizer for the <see cref="WebRtcApmModifier"/>.
    /// </summary>
    ~WebRtcApmModifier() => Dispose(false);

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}