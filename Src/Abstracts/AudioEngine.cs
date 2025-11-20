using SoundFlow.Abstracts.Devices;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;
using SoundFlow.Utils;
using System.ComponentModel;
using SoundFlow.Midi.Devices;
using SoundFlow.Midi.Routing;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs.Events;

namespace SoundFlow.Abstracts;

/// <summary>
/// This event is raised when samples are processed by Input or Output components.
/// </summary>
public delegate void AudioProcessCallback(Span<float> samples, Capability capability);

/// <summary>
/// The base class for audio engines. An engine acts as a context manager for audio devices
/// and provides facilities for decoding and encoding audio via a pluggable codec system.
/// </summary>
public abstract class AudioEngine : IDisposable
{
    private SoundComponent? _soloedComponent;
    private readonly object _lock = new();
    private readonly Dictionary<string, List<CodecRegistration>> _codecRegistry = new();
    private long _registrationCounter;
    private IMidiBackend? _midiBackend;

    /// <summary>
    /// Internal wrapper to store a factory along with its mutable priority and registration order.
    /// </summary>
    private class CodecRegistration(ICodecFactory factory, long registrationOrder)
    {
        public ICodecFactory Factory { get; } = factory;
        public int Priority { get; set; } = factory.Priority; // Use the factory's suggested priority by default.
        public long RegistrationOrder { get; } = registrationOrder;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioEngine"/> class.
    /// </summary>
    protected AudioEngine()
    {
        MidiManager = new MidiManager(this);
    }

    /// <summary>
    /// Gets an array of available playback devices.
    /// </summary>
    public DeviceInfo[] PlaybackDevices { get; protected set; } = [];

    /// <summary>
    /// Gets an array of available capture devices.
    /// </summary>
    public DeviceInfo[] CaptureDevices { get; protected set; } = [];

    /// <summary>
    /// Gets an array of available MIDI input devices.
    /// </summary>
    public MidiDeviceInfo[] MidiInputDevices { get; protected set; } = [];

    /// <summary>
    /// Gets an array of available MIDI output devices.
    /// </summary>
    public MidiDeviceInfo[] MidiOutputDevices { get; protected set; } = [];

    /// <summary>
    /// Gets the central manager for all MIDI devices and routing.
    /// </summary>
    public MidiManager MidiManager { get; }

    /// <summary>
    /// Cleans up the audio backend context.
    /// </summary>
    protected abstract void CleanupBackend();

    #region Synchronization Events

    /// <summary>
    /// Occurs when an audio device starts processing audio.
    /// This can be used by external components (like a MIDI backend) to send synchronization messages (e.g., MIDI Start).
    /// </summary>
    public event EventHandler<DeviceEventArgs>? DeviceStarted;

    /// <summary>
    /// Occurs when an audio device stops processing audio.
    /// This can be used by external components to send synchronization messages (e.g., MIDI Stop).
    /// </summary>
    public event EventHandler<DeviceEventArgs>? DeviceStopped;

    /// <summary>
    /// Occurs after a block of audio has been prepared for rendering by a playback device.
    /// This event provides a sample-accurate clock tick for driving master synchronization (e.g., MIDI Clock).
    /// </summary>
    public event EventHandler<AudioFramesRenderedEventArgs>? AudioFramesRendered;

    /// <summary>
    /// For internal use by audio device implementations. Raises the <see cref="DeviceStarted"/> event.
    /// </summary>
    /// <param name="device">The device that has started.</param>
    internal void RaiseDeviceStarted(AudioDevice device) => DeviceStarted?.Invoke(this, new DeviceEventArgs(device));

    /// <summary>
    /// For internal use by audio device implementations. Raises the <see cref="DeviceStopped"/> event.
    /// </summary>
    /// <param name="device">The device that has stopped.</param>
    internal void RaiseDeviceStopped(AudioDevice device) => DeviceStopped?.Invoke(this, new DeviceEventArgs(device));

    /// <summary>
    /// For internal use by audio device implementations. Raises the <see cref="AudioFramesRendered"/> event
    /// using a reusable EventArgs object to avoid allocation overhead in the hot path.
    /// </summary>
    /// <param name="args">The cached event arguments object.</param>
    internal void RaiseAudioFramesRendered(AudioFramesRenderedEventArgs args) => 
        AudioFramesRendered?.Invoke(this, args);

    #endregion


    /// <summary>
    /// Configures the engine to use a specific MIDI backend implementation. This method must be called
    /// before any MIDI operations are performed.
    /// </summary>
    /// <param name="midiBackend">The MIDI backend instance to use.</param>
    /// <exception cref="InvalidOperationException">Thrown if a MIDI backend has already been configured.</exception>
    public void UseMidiBackend(IMidiBackend midiBackend)
    {
        if (_midiBackend != null)
            throw new InvalidOperationException("A MIDI backend has already been configured for this engine.");

        _midiBackend = midiBackend;
        _midiBackend.Initialize(this);
    }

    /// <summary>
    /// Solos the specified sound component, muting all other components within this engine's devices.
    /// </summary>
    /// <param name="component">The component to solo.</param>
    public void SoloComponent(SoundComponent component)
    {
        lock (_lock)
        {
            _soloedComponent = component;
        }
    }

    /// <summary>
    /// Unsolos the specified sound component.
    /// </summary>
    /// <param name="component">The component to unsolo.</param>
    public void UnsoloComponent(SoundComponent component)
    {
        lock (_lock)
        {
            if (_soloedComponent == component)
            {
                _soloedComponent = null;
            }
        }
    }

    /// <summary>
    /// Gets the currently soloed component, if any.
    /// </summary>
    /// <returns>The soloed SoundComponent or null.</returns>
    public SoundComponent? GetSoloedComponent()
    {
        lock (_lock)
        {
            return _soloedComponent;
        }
    }

    /// <summary>
    /// Registers a codec factory with the audio engine using its default priority.
    /// </summary>
    /// <param name="factory">The codec factory to register.</param>
    public void RegisterCodecFactory(ICodecFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (_codecRegistry)
        {
            var registration = new CodecRegistration(factory, _registrationCounter++);
            foreach (var formatId in factory.SupportedFormatIds)
            {
                if (!_codecRegistry.TryGetValue(formatId, out var registrationList))
                {
                    registrationList = [];
                    _codecRegistry[formatId] = registrationList;
                }

                registrationList.Add(registration);
            }
        }
    }

    /// <summary>
    /// Unregisters a codec factory from the engine using its unique ID.
    /// </summary>
    /// <param name="factoryId">The unique ID of the factory to unregister.</param>
    /// <returns><c>true</c> if the factory was found and removed; otherwise, <c>false</c>.</returns>
    public bool UnregisterCodecFactory(string factoryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(factoryId);

        lock (_codecRegistry)
        {
            // Sum the number of removed items from all lists, if the total is greater than 0, a factory was found and removed.
            var totalRemoved = _codecRegistry.Values.Sum(registrationList =>
                registrationList.RemoveAll(reg => reg.Factory.FactoryId == factoryId)
            );
            return totalRemoved > 0;
        }
    }

    /// <summary>
    /// Sets a new priority for a registered codec factory, overriding its default priority.
    /// This allows for runtime prioritization of codecs.
    /// </summary>
    /// <param name="factoryId">The unique ID of the factory to prioritize.</param>
    /// <param name="newPriority">The new priority value. Higher numbers are tried first.</param>
    /// <returns><c>true</c> if the factory was found and its priority was updated; otherwise, <c>false</c>.</returns>
    public bool SetCodecPriority(string factoryId, int newPriority)
    {
        ArgumentException.ThrowIfNullOrEmpty(factoryId);

        lock (_codecRegistry)
        {
            // Select all registrations for the given factory ID and convert to a list.
            var registrationsToUpdate = _codecRegistry.Values
                .SelectMany(list => list.Where(reg => reg.Factory.FactoryId == factoryId))
                .ToList();

            // Update the priority of each registration.
            registrationsToUpdate.ForEach(reg => reg.Priority = newPriority);

            // Return true if the list we acted on was not empty.
            return registrationsToUpdate.Count != 0;
        }
    }

    /// <summary>
    /// Gets a read-only list of all registered codec factories for a specific format,
    /// ordered from highest to lowest priority.
    /// </summary>
    /// <param name="formatId">The format identifier (e.g., "flac").</param>
    /// <returns>A read-only list of factories, or an empty list if none are registered.</returns>
    public IReadOnlyList<ICodecFactory> GetRegisteredCodecs(string formatId)
    {
        lock (_codecRegistry)
        {
            if (_codecRegistry.TryGetValue(formatId, out var registrationList))
            {
                return registrationList
                    .OrderByDescending(r => r.Priority)
                    .ThenByDescending(r => r.RegistrationOrder) // For tie-breaking
                    .Select(r => r.Factory)
                    .ToList()
                    .AsReadOnly();
            }
        }

        return [];
    }

    /// <summary>
    /// Constructs a sound encoder for the specified format. It queries registered codec factories
    /// based on priority to find a suitable implementation.
    /// </summary>
    /// <param name="stream">The stream to write encoded audio to.</param>
    /// <param name="formatId">The string identifier for the desired audio format (e.g., "wav", "flac").</param>
    /// <param name="format">The audio format of the source PCM data.</param>
    /// <returns>An instance of a sound encoder.</returns>
    /// <exception cref="NotSupportedException">Thrown if no registered and working codec factory is found for the specified format.</exception>
    public ISoundEncoder CreateEncoder(Stream stream, string formatId, AudioFormat format)
    {
        List<CodecRegistration>? registrationList;
        lock (_codecRegistry)
        {
            _codecRegistry.TryGetValue(formatId, out registrationList);
        }

        if (registrationList != null)
        {
            var sortedFactories = registrationList
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.RegistrationOrder);

            foreach (var registration in sortedFactories)
            {
                try
                {
                    var encoder = registration.Factory.CreateEncoder(stream, formatId, format);
                    if (encoder != null)
                        return encoder;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        $"Codec factory '{registration.Factory.FactoryId}' failed to create an encoder for format '{formatId}': {ex.Message}");
                }
            }
        }

        throw new NotSupportedException(
            $"No registered and working codec factory found for encoding format '{formatId}'.");
    }

    /// <summary>
    /// Constructs a sound decoder for the specified format. It queries registered codec factories
    /// based on priority to find a suitable implementation.
    /// </summary>
    /// <param name="stream">The stream containing the audio data.</param>
    /// <param name="formatId">The string identifier for the audio format (e.g., "mp3", "flac").</param>
    /// <param name="format">The audio format containing channels, sample rate, and sample format.</param>
    /// <returns>An instance of a sound decoder.</returns>
    /// <exception cref="NotSupportedException">Thrown if no registered and working codec factory is found for the specified format.</exception>
    public ISoundDecoder CreateDecoder(Stream stream, string formatId, AudioFormat format)
    {
        List<CodecRegistration>? registrationList;
        lock (_codecRegistry)
        {
            _codecRegistry.TryGetValue(formatId, out registrationList);
        }

        if (registrationList != null)
        {
            var sortedFactories = registrationList
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.RegistrationOrder);

            foreach (var registration in sortedFactories)
            {
                try
                {
                    var decoder = registration.Factory.CreateDecoder(stream, formatId, format);
                    if (decoder != null)
                        return decoder;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        $"Codec factory '{registration.Factory.FactoryId}' failed to create a decoder for format '{formatId}': {ex.Message}");
                }
            }
        }

        throw new NotSupportedException(
            $"No registered and working codec factory found for decoding format '{formatId}'.");
    }

    /// <summary>
    /// Constructs a sound decoder by probing the stream with all registered codec factories.
    /// This method is used when the audio format is not known beforehand. Factories are tried in order of priority.
    /// </summary>
    /// <param name="stream">The stream containing the audio data. Must be readable and seekable.</param>
    /// <param name="detectedFormat">When this method returns, contains the audio format detected by the successful decoder.</param>
    /// <param name="hintFormat">An optional hint for the desired output audio format. The factory should attempt to produce this format if possible.</param>
    /// <returns>An instance of a sound decoder.</returns>
    /// <exception cref="NotSupportedException">Thrown if no registered codec factory could decode the provided stream.</exception>
    public ISoundDecoder CreateDecoder(Stream stream, out AudioFormat detectedFormat, AudioFormat? hintFormat = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        List<ICodecFactory> allFactories;
        lock (_codecRegistry)
        {
            allFactories = _codecRegistry.Values
                .SelectMany(list => list)
                .DistinctBy(reg => reg.Factory.FactoryId)
                .OrderByDescending(r => r.Priority)
                .ThenBy(r => r.RegistrationOrder)
                .Select(r => r.Factory)
                .ToList();
        }

        foreach (var factory in allFactories)
        {
            if (stream.CanSeek) stream.Position = 0;
            try
            {
                var decoder = factory.TryCreateDecoder(stream, out detectedFormat, hintFormat);
                if (decoder != null)
                    return decoder;
            }
            catch (Exception ex)
            {
                Log.Warning($"Codec factory '{factory.FactoryId}' failed during probing: {ex.Message}");
            }
        }

        throw new NotSupportedException("No registered codec factory could decode the provided stream.");
    }

    /// <summary>
    /// Initializes and returns a playback device.
    /// </summary>
    /// <param name="deviceInfo">The device to initialize. Must be a playback-capable device.</param>
    /// <param name="format">The desired audio format.</param>
    /// <param name="config">Optional detailed configuration for the device and its backend.</param>
    /// <returns>An initialized <see cref="AudioPlaybackDevice"/>.</returns>
    public abstract AudioPlaybackDevice InitializePlaybackDevice(DeviceInfo? deviceInfo, AudioFormat format,
        DeviceConfig? config = null);

    /// <summary>
    /// Initializes and returns a capture device.
    /// </summary>
    /// <param name="deviceInfo">The device to initialize. Must be a capture-capable device.</param>
    /// <param name="format">The desired audio format.</param>
    /// <param name="config">Optional detailed configuration for the device and its backend.</param>
    /// <returns>An initialized <see cref="AudioCaptureDevice"/>.</returns>
    public abstract AudioCaptureDevice InitializeCaptureDevice(DeviceInfo? deviceInfo, AudioFormat format,
        DeviceConfig? config = null);

    /// <summary>
    /// Initializes a high-level full-duplex device for simultaneous input and output.
    /// This simplifies live effects processing by managing a paired capture and playback device.
    /// </summary>
    /// <param name="playbackDeviceInfo">The playback device to use. Use null for the system default.</param>
    /// <param name="captureDeviceInfo">The capture device to use. Use null for the system default.</param>
    /// <param name="format">The audio format to use for both devices.</param>
    /// <param name="config">Optional detailed configuration for the devices.</param>
    /// <returns>An initialized <see cref="FullDuplexDevice"/> ready for use.</returns>
    public abstract FullDuplexDevice InitializeFullDuplexDevice(DeviceInfo? playbackDeviceInfo,
        DeviceInfo? captureDeviceInfo, AudioFormat format, DeviceConfig? config = null);

    /// <summary>
    /// Initializes a loopback capture device, allowing for the recording of system audio output.
    /// </summary>
    /// <param name="format">The desired audio format for the loopback capture.</param>
    /// <param name="config">Optional detailed configuration for the device.</param>
    /// <returns>An initialized <see cref="AudioCaptureDevice"/> configured for loopback recording.</returns>
    /// <exception cref="NotSupportedException">Thrown if a default playback device (required for loopback) cannot be found.</exception>
    public abstract AudioCaptureDevice InitializeLoopbackDevice(AudioFormat format, DeviceConfig? config = null);

    /// <summary>
    /// Switches an active playback device to a new physical device, preserving its audio graph.
    /// The old device instance will be disposed.
    /// </summary>
    /// <param name="oldDevice">The playback device instance to replace.</param>
    /// <param name="newDeviceInfo">The info for the new physical device to use.</param>
    /// <param name="config">Optional configuration for the new device.</param>
    /// <returns>A new, active <see cref="AudioPlaybackDevice"/> instance.</returns>
    public abstract AudioPlaybackDevice SwitchDevice(AudioPlaybackDevice oldDevice, DeviceInfo newDeviceInfo,
        DeviceConfig? config = null);

    /// <summary>
    /// Switches an active capture device to a new physical device, preserving its event subscribers.
    /// The old device instance will be disposed.
    /// </summary>
    /// <param name="oldDevice">The capture device instance to replace.</param>
    /// <param name="newDeviceInfo">The info for the new physical device to use.</param>
    /// <param name="config">Optional configuration for the new device.</param>
    /// <returns>A new, active <see cref="AudioCaptureDevice"/> instance.</returns>
    public abstract AudioCaptureDevice SwitchDevice(AudioCaptureDevice oldDevice, DeviceInfo newDeviceInfo,
        DeviceConfig? config = null);

    /// <summary>
    /// Switches the devices used by a full-duplex instance, preserving its state.
    /// The old duplex device instance will be disposed.
    /// </summary>
    /// <param name="oldDevice">The full-duplex device instance to replace.</param>
    /// <param name="newPlaybackInfo">Info for the new playback device. If null, the existing playback device is used.</param>
    /// <param name="newCaptureInfo">Info for the new capture device. If null, the existing capture device is used.</param>
    /// <param name="config">Optional configuration for the new device(s).</param>
    /// <returns>A new, active <see cref="FullDuplexDevice"/> instance.</returns>
    public abstract FullDuplexDevice SwitchDevice(FullDuplexDevice oldDevice, DeviceInfo? newPlaybackInfo,
        DeviceInfo? newCaptureInfo, DeviceConfig? config = null);

    /// <summary>
    /// For backend implementers only. Creates and returns a MIDI input device.
    /// Application developers should use the <see cref="MidiManager"/> for all MIDI operations.
    /// </summary>
    /// <param name="deviceInfo">The MIDI device to create.</param>
    /// <returns>An initialized <see cref="MidiInputDevice"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual MidiInputDevice CreateMidiInputDevice(MidiDeviceInfo deviceInfo)
    {
        return _midiBackend == null
            ? throw new NotSupportedException(
                "No MIDI backend has been configured. Use engine.UseMidiBackend() to enable MIDI functionality.")
            : _midiBackend.CreateMidiInputDevice(deviceInfo);
    }

    /// <summary>
    /// For backend implementers only. Creates and returns a MIDI output device.
    /// Application developers should use the <see cref="MidiManager"/> for all MIDI operations.
    /// </summary>
    /// <param name="deviceInfo">The MIDI device to create.</param>
    /// <returns>An initialized <see cref="MidiOutputDevice"/>.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual MidiOutputDevice CreateMidiOutputDevice(MidiDeviceInfo deviceInfo)
    {
        return _midiBackend == null
            ? throw new NotSupportedException(
                "No MIDI backend has been configured. Use engine.UseMidiBackend() to enable MIDI functionality.")
            : _midiBackend.CreateMidiOutputDevice(deviceInfo);
    }

    /// <summary>
    /// Retrieves the list of available playback and capture devices from the underlying audio backend.
    /// </summary>
    [Obsolete($"Use {nameof(UpdateAudioDevicesInfo)} or {nameof(UpdateMidiDevicesInfo)} instead. This method will be removed in the next major version update.")]
    public void UpdateDevicesInfo()
    {
        UpdateAudioDevicesInfo();
        UpdateMidiDevicesInfo();
    }

    /// <summary>
    /// Retrieves the list of available playback and capture devices from the underlying audio backend.
    /// </summary>
    public abstract void UpdateAudioDevicesInfo();

    /// <summary>
    /// Retrieves the list of available MIDI input and output devices from the underlying MIDI backend.
    /// </summary>
    public virtual void UpdateMidiDevicesInfo()
    {
        if (_midiBackend != null)
        {
            _midiBackend.UpdateMidiDevicesInfo(out var inputs, out var outputs);
            MidiInputDevices = inputs;
            MidiOutputDevices = outputs;
        }
        else
        {
            MidiInputDevices = [];
            MidiOutputDevices = [];
        }
    }

    #region IDisposable Support

    /// <summary>
    /// Gets a value indicating whether the audio engine has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Cleans up resources before the object is garbage collected.
    /// </summary>
    ~AudioEngine()
    {
        Dispose(false);
    }

    /// <summary>
    /// Disposes of managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        if (disposing)
        {
            MidiManager.Dispose();
            _midiBackend?.Dispose();
            CleanupBackend();
        }

        IsDisposed = true;
    }

    /// <summary>
    /// Disposes of the audio engine and all associated devices.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}