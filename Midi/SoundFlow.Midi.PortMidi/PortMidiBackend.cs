using System.Runtime.InteropServices;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Editing;
using SoundFlow.Interfaces;
using SoundFlow.Midi.Devices;
using SoundFlow.Midi.PortMidi.Devices;
using SoundFlow.Midi.PortMidi.Enums;
using SoundFlow.Midi.PortMidi.Exceptions;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs.Events;

namespace SoundFlow.Midi.PortMidi;

/// <summary>
/// A concrete implementation of <see cref="IMidiBackend"/> using the cross-platform PortMidi library.
/// This backend provides MIDI I/O capabilities and advanced synchronization features.
/// </summary>
public sealed class PortMidiBackend : IMidiBackend
{
    private AudioEngine? _engine;
    private readonly MidiSyncManager _syncManager;
    private readonly Dictionary<MidiDeviceInfo, PortMidiInputDevice> _activeSyncInputs = new();
    private readonly Dictionary<MidiDeviceInfo, PortMidiOutputDevice> _activeSyncOutputs = new();

    // State for managing multi-device synchronization
    private readonly object _syncLock = new();
    private int _activePlaybackDeviceCount;
    private AudioDevice? _masterClockSourceDevice;
    private readonly List<AudioPlaybackDevice> _activePlaybackDevices = new();

    /// <summary>
    /// Gets the current MIDI synchronization mode.
    /// </summary>
    public SyncMode CurrentSyncMode => _syncManager.Mode;

    /// <summary>
    /// Gets the current lock status of the MIDI synchronization.
    /// </summary>
    public SyncStatus CurrentSyncStatus => _syncManager.Status;

    /// <summary>
    /// Gets the tempo in Beats Per Minute (BPM) detected from an external MIDI Clock source.
    /// Returns 0 if not in MIDI Clock slave mode or if no stable clock is detected.
    /// </summary>
    public double DetectedBpm => _syncManager.DetectedBpm;

    /// <summary>
    /// Occurs when the MIDI synchronization lock status changes.
    /// </summary>
    public event Action<SyncStatus>? OnSyncStatusChanged
    {
        add => _syncManager.OnSyncStatusChanged += value;
        remove => _syncManager.OnSyncStatusChanged -= value;
    }

    /// <summary>
    /// Occurs when the detected BPM from an external MIDI Clock changes.
    /// </summary>
    public event Action<double>? OnBpmChanged
    {
        add => _syncManager.OnBpmChanged += value;
        remove => _syncManager.OnBpmChanged -= value;
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="PortMidiBackend"/> class.
    /// </summary>
    public PortMidiBackend()
    {
        var result = (PortMidiError)Native.Pm_Initialize();
        if (result != PortMidiError.NoError)
            throw new PortBackendException(result);

        _syncManager = new MidiSyncManager();
    }

    /// <inheritdoc />
    public void Initialize(AudioEngine engine)
    {
        _engine = engine;
        _engine.DeviceStarted += HandleDeviceStarted;
        _engine.DeviceStopped += HandleDeviceStopped;
        _engine.AudioFramesRendered += HandleAudioFramesRendered;
    }

    /// <inheritdoc />
    public MidiInputDevice CreateMidiInputDevice(MidiDeviceInfo deviceInfo)
    {
        return new PortMidiInputDevice(deviceInfo);
    }

    /// <inheritdoc />
    public MidiOutputDevice CreateMidiOutputDevice(MidiDeviceInfo deviceInfo)
    {
        return new PortMidiOutputDevice(deviceInfo);
    }
    
    /// <inheritdoc />
    public void UpdateMidiDevicesInfo(out MidiDeviceInfo[] inputs, out MidiDeviceInfo[] outputs)
    {
        var deviceCount = Native.Pm_CountDevices();
        if (deviceCount < 0)
            throw new PortBackendException((PortMidiError)deviceCount);

        var inputDevices = new List<MidiDeviceInfo>();
        var outputDevices = new List<MidiDeviceInfo>();

        for (var i = 0; i < deviceCount; i++)
        {
            var pDeviceInfo = Native.Pm_GetDeviceInfo(i);
            if (pDeviceInfo == nint.Zero) continue;

            var deviceInfo = Marshal.PtrToStructure<Native.PmDeviceInfo>(pDeviceInfo);
            var name = Marshal.PtrToStringAnsi(deviceInfo.Name) ?? "Unknown MIDI Device";

            if (deviceInfo.Input != 0)
            {
                inputDevices.Add(new MidiDeviceInfo { Id = i, Name = name });
            }

            if (deviceInfo.Output != 0)
            {
                outputDevices.Add(new MidiDeviceInfo { Id = i, Name = name });
            }
        }

        inputs = inputDevices.ToArray();
        outputs = outputDevices.ToArray();
    }
    
    /// <summary>
    /// Configures the engine's MIDI synchronization behavior.
    /// </summary>
    /// <param name="mode">The desired synchronization mode (Master, Slave, or Off).</param>
    /// <param name="source">The synchronization source to use when in Slave mode (MIDI Clock or MTC).</param>
    /// <param name="inputDeviceInfo">The MIDI input device to receive sync messages from (for Slave mode).</param>
    /// <param name="outputDeviceInfo">The MIDI output device to send sync messages to (for Master mode).</param>
    /// <param name="renderer">The composition renderer to be controlled or to act as the master clock source.</param>
    public void ConfigureSync(SyncMode mode, SyncSource source, MidiDeviceInfo? inputDeviceInfo,
        MidiDeviceInfo? outputDeviceInfo, CompositionRenderer? renderer)
    {
        PortMidiInputDevice? inputDevice = null;
        if (mode == SyncMode.Slave && inputDeviceInfo.HasValue)
        {
            if (!_activeSyncInputs.TryGetValue(inputDeviceInfo.Value, out var existingDevice))
            {
                existingDevice = new PortMidiInputDevice(inputDeviceInfo.Value);
                _activeSyncInputs[inputDeviceInfo.Value] = existingDevice;
            }

            inputDevice = existingDevice;
        }

        PortMidiOutputDevice? outputDevice = null;
        if (mode == SyncMode.Master && outputDeviceInfo.HasValue)
        {
            if (!_activeSyncOutputs.TryGetValue(outputDeviceInfo.Value, out var existingDevice))
            {
                existingDevice = new PortMidiOutputDevice(outputDeviceInfo.Value);
                _activeSyncOutputs[outputDeviceInfo.Value] = existingDevice;
            }

            outputDevice = existingDevice;
        }

        _syncManager.Configure(mode, source, inputDevice, outputDevice, renderer);
    }

    private void HandleDeviceStarted(object? sender, DeviceEventArgs e)
    {
        if (e.Device is not AudioPlaybackDevice playbackDevice) return;

        lock (_syncLock)
        {
            _activePlaybackDevices.Add(playbackDevice);
            _activePlaybackDeviceCount++;

            if (_activePlaybackDeviceCount == 1)
            {
                _syncManager.StartSendingClock();
                _masterClockSourceDevice = playbackDevice;
            }
        }
    }

    private void HandleDeviceStopped(object? sender, DeviceEventArgs e)
    {
        if (e.Device is not AudioPlaybackDevice playbackDevice) return;

        lock (_syncLock)
        {
            _activePlaybackDevices.Remove(playbackDevice);
            _activePlaybackDeviceCount--;

            if (_activePlaybackDeviceCount == 0)
            {
                _syncManager.StopSendingClock();
                _masterClockSourceDevice = null;
            }
            else if (e.Device == _masterClockSourceDevice)
            {
                _masterClockSourceDevice = _activePlaybackDevices.FirstOrDefault();
            }
        }
    }

    private void HandleAudioFramesRendered(object? sender, AudioFramesRenderedEventArgs e)
    {
        if (e.Device == _masterClockSourceDevice)
            _syncManager.UpdateMasterClock(e.FrameCount);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_engine != null)
        {
            _engine.DeviceStarted -= HandleDeviceStarted;
            _engine.DeviceStopped -= HandleDeviceStopped;
            _engine.AudioFramesRendered -= HandleAudioFramesRendered;
        }
        
        _syncManager.Dispose();
        foreach (var device in _activeSyncInputs.Values)
        {
            device.Dispose();
        }
        _activeSyncInputs.Clear();

        foreach (var device in _activeSyncOutputs.Values)
        {
            device.Dispose();
        }
        _activeSyncOutputs.Clear();
        
        lock (_syncLock)
        {
            _activePlaybackDevices.Clear();
            _activePlaybackDeviceCount = 0;
            _masterClockSourceDevice = null;
        }

        Native.Pm_Terminate();
    }
}