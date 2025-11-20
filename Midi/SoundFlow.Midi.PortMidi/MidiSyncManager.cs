using SoundFlow.Editing;
using SoundFlow.Midi.PortMidi.Devices;
using SoundFlow.Midi.PortMidi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Midi.PortMidi;

/// <summary>
/// Manages MIDI synchronization (Clock and MTC) for a PortMidi-based audio engine.
/// This class acts as the bridge between low-level MIDI messages and the composition's transport.
/// </summary>
internal sealed class MidiSyncManager : IDisposable
{
    private readonly object _lock = new();

    // Configuration State
    private PortMidiInputDevice? _syncInputDevice;
    private PortMidiOutputDevice? _syncOutputDevice;
    private CompositionRenderer? _compositionRenderer;

    // Sync State Properties
    public SyncMode Mode { get; private set; } = SyncMode.Off;
    public SyncSource Source { get; private set; } = SyncSource.Internal;
    public SyncStatus Status { get; private set; } = SyncStatus.Unlocked;
    public double DetectedBpm { get; private set; }

    // Events
    public event Action<SyncStatus>? OnSyncStatusChanged;
    public event Action<double>? OnBpmChanged;

    // MIDI Clock Slave State
    private readonly List<long> _bpmIntervals = new();
    private long _lastClockTimestamp;
    private int _midiClocksReceived;
    private bool _isPlaying;

    // MIDI Clock Master State
    private double _samplesPerMidiTick;
    private double _masterClockSampleCounter;
    private bool _isSendingClock;

    // MTC State
    private readonly int[] _mtcNibbles = new int[8];
    private int _mtcQuarterFramesReceived;

    /// <summary>
    /// Configures the synchronization settings.
    /// </summary>
    public void Configure(SyncMode mode, SyncSource source, PortMidiInputDevice? inputDevice, PortMidiOutputDevice? outputDevice, CompositionRenderer? renderer)
    {
        lock (_lock)
        {
            // Clean up previous configuration
            if (_syncInputDevice != null) 
                _syncInputDevice.OnSystemMessageReceived -= ProcessSystemMessage;

            ResetState();

            Mode = mode;
            Source = source;
            _syncInputDevice = inputDevice;
            _syncOutputDevice = outputDevice;
            _compositionRenderer = renderer;

            if (_compositionRenderer != null) 
                _compositionRenderer.IsSyncDriven = Mode == SyncMode.Slave;

            if (Mode == SyncMode.Slave && _syncInputDevice != null) 
                _syncInputDevice.OnSystemMessageReceived += ProcessSystemMessage;
        }
    }

    /// <summary>
    /// Processes an incoming system-level MIDI message for synchronization purposes.
    /// </summary>
    public void ProcessSystemMessage(int message, long timestamp)
    {
        if (Mode != SyncMode.Slave || _compositionRenderer == null) return;
        
        var statusByte = (byte)(message & 0xFF);

        switch (Source)
        {
            case SyncSource.MidiClock:
                ProcessMidiClockMessage(statusByte, message, timestamp);
                break;
            case SyncSource.Mtc:
                ProcessMtcMessage(statusByte, message);
                break;
        }
    }
    
    /// <summary>
    /// In Master mode, this method is called by the audio engine's render loop to drive the MIDI clock output.
    /// </summary>
    /// <param name="framesRendered">The number of audio frames rendered in the last block.</param>
    public void UpdateMasterClock(int framesRendered)
    {
        if (Mode != SyncMode.Master || !_isSendingClock || _syncOutputDevice == null || _compositionRenderer == null) return;

        // If the tempo has changed in the composition, we must update our tick rate.
        var currentTempo = _compositionRenderer.GetTempoAtCurrentPosition().BeatsPerMinute;
        if (Math.Abs(currentTempo - DetectedBpm) > 0.01)
        {
            UpdateTempo(currentTempo);
            DetectedBpm = currentTempo;
        }

        _masterClockSampleCounter += framesRendered;
        while (_masterClockSampleCounter >= _samplesPerMidiTick)
        {
            // Send a clock tick
            _syncOutputDevice.SendMessage(new MidiMessage(0xF8, 0, 0));
            _masterClockSampleCounter -= _samplesPerMidiTick;
        }
    }

    private void ProcessMidiClockMessage(byte statusByte, int message, long timestamp)
    {
        switch (statusByte)
        {
            case 0xFA: // Start
                _isPlaying = true;
                _compositionRenderer?.SyncPlay();
                ResetClockState();
                SetSyncStatus(SyncStatus.Locked);
                break;

            case 0xFB: // Stop
                _isPlaying = false;
                _compositionRenderer?.SyncStop();
                ResetClockState();
                SetSyncStatus(SyncStatus.Unlocked);
                break;

            case 0xFC: // Continue
                _isPlaying = true;
                _compositionRenderer?.SyncContinue();
                SetSyncStatus(SyncStatus.Locked);
                break;

            case 0xF8: // Clock
                if (!_isPlaying) return;
                
                _compositionRenderer?.AdvanceBySyncTicks(1);
                _midiClocksReceived++;

                if (_lastClockTimestamp > 0)
                {
                    var interval = timestamp - _lastClockTimestamp;
                    _bpmIntervals.Add(interval);
                    if (_bpmIntervals.Count > 48) // Average over 2 quarter notes
                        _bpmIntervals.RemoveAt(0);
                    
                    if (_midiClocksReceived % 24 == 0) // Update BPM every quarter note
                        UpdateBpm();
                }
                _lastClockTimestamp = timestamp;
                break;

            case 0xF2: // Song Position Pointer
                var data1 = (byte)((message >> 8) & 0xFF);
                var data2 = (byte)((message >> 16) & 0xFF);
                var spp = (data2 << 7) | data1; // 14-bit value
                if (_compositionRenderer != null && DetectedBpm > 0)
                {
                    // SPP measures "MIDI beats" (1 beat = 6 MIDI clocks, or one 16th note).
                    var timeInSeconds = spp * (60.0 / (DetectedBpm * 4.0));
                    _compositionRenderer.SyncSeek(TimeSpan.FromSeconds(timeInSeconds));
                }
                break;
        }
    }

    private void ProcessMtcMessage(byte statusByte, int message)
    {
        // MTC Quarter Frame (0xF1)
        if (statusByte == 0xF1)
        {
            var data = (byte)((message >> 8) & 0xFF);
            var messageType = (data >> 4) & 0x7;
            var value = data & 0xF;
            _mtcNibbles[messageType] = value;
            _mtcQuarterFramesReceived++;

            // A full timecode message is sent every 2 frames. We only need to reconstruct and seek when we have all 8 pieces.
            if (messageType == 7 && _mtcQuarterFramesReceived >= 8)
            {
                ReconstructMtcTime();
                _mtcQuarterFramesReceived = 0;
                SetSyncStatus(SyncStatus.Locked);
            }
        }
        else if (statusByte == 0xFA) // MTC is often accompanied by standard transport
        {
            _compositionRenderer?.SyncPlay();
        }
        else if (statusByte == 0xFB)
        {
            _compositionRenderer?.SyncStop();
            SetSyncStatus(SyncStatus.Unlocked);
        }
    }

    private void ReconstructMtcTime()
    {
        var frames = _mtcNibbles[0] | (_mtcNibbles[1] << 4);
        var seconds = _mtcNibbles[2] | (_mtcNibbles[3] << 4);
        var minutes = _mtcNibbles[4] | (_mtcNibbles[5] << 4);
        var hoursAndType = _mtcNibbles[6] | (_mtcNibbles[7] << 4);

        var hours = hoursAndType & 0x1F;
        var frameRateType = (hoursAndType >> 5) & 0x03;
        
        var frameRate = frameRateType switch
        {
            0 => 24,
            1 => 25,
            2 => 29.97,
            _ => 30
        };

        var time = new TimeSpan(0, hours, minutes, seconds, (int)(frames * (1000.0 / frameRate)));
        _compositionRenderer?.SyncSeek(time);
    }

    public void StartSendingClock()
    {
        if (Mode != SyncMode.Master || _syncOutputDevice == null || _compositionRenderer == null) return;
        
        UpdateTempo(_compositionRenderer.GetTempoAtCurrentPosition().BeatsPerMinute);
        _syncOutputDevice.SendMessage(new MidiMessage(0xFA, 0, 0));
        _isSendingClock = true;
    }

    public void StopSendingClock()
    {
        if (Mode != SyncMode.Master || _syncOutputDevice == null) return;

        _isSendingClock = false;
        _syncOutputDevice.SendMessage(new MidiMessage(0xFB, 0, 0));
    }

    /// <summary>
    /// Called when the composition's tempo changes in Master mode.
    /// </summary>
    public void UpdateTempo(double bpm)
    {
        if (Mode != SyncMode.Master || bpm <= 0 || _compositionRenderer == null) return;
        
        var sampleRate = _compositionRenderer.SampleRate;
        _samplesPerMidiTick = sampleRate * 60.0 / (bpm * 24.0);
    }
    
    private void UpdateBpm()
    {
        if (_bpmIntervals.Count == 0) return;

        var averageInterval = _bpmIntervals.Average();
        if (averageInterval > 0)
        {
            var newBpm = 60000.0 / (averageInterval * 24.0);
            if (Math.Abs(DetectedBpm - newBpm) > 0.1)
            {
                DetectedBpm = newBpm;
                OnBpmChanged?.Invoke(DetectedBpm);
            }
        }
    }

    private void SetSyncStatus(SyncStatus newStatus)
    {
        if (Status == newStatus) return;
        Status = newStatus;
        OnSyncStatusChanged?.Invoke(Status);
    }

    private void ResetClockState()
    {
        _midiClocksReceived = 0;
        _lastClockTimestamp = 0;
        _bpmIntervals.Clear();
        DetectedBpm = 0;
        OnBpmChanged?.Invoke(0);
    }
    
    private void ResetState()
    {
        Mode = SyncMode.Off;
        Source = SyncSource.Internal;
        _syncInputDevice = null;
        _syncOutputDevice = null;
        if (_compositionRenderer != null) 
            _compositionRenderer.IsSyncDriven = false;
        _compositionRenderer = null;
        
        _isSendingClock = false;
        _isPlaying = false;
        
        ResetClockState();
        SetSyncStatus(SyncStatus.Unlocked);
    }

    public void Dispose()
    {
        Configure(SyncMode.Off, SyncSource.Internal, null, null, null);
    }
}