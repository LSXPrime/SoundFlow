using System.Collections.Concurrent;
using System.Diagnostics;
using SoundFlow.Metadata.Midi;
using SoundFlow.Metadata.Midi.Enums;
using SoundFlow.Midi.Devices;
using SoundFlow.Midi.Structs;
using SoundFlow.Providers;

namespace SoundFlow.Midi;

/// <summary>
/// A delegate that defines the contract for converting a real-time TimeSpan into a MIDI tick value.
/// </summary>
/// <param name="time">The time to convert.</param>
/// <returns>The corresponding value in MIDI ticks.</returns>
public delegate long TimeToTickConverter(TimeSpan time);

/// <summary>
/// A non-audio component that captures and timestamps MIDI messages with high-resolution
/// timing. This component is standalone and relies on its consumer to provide the logic for
/// converting time to MIDI ticks.
/// </summary>
public sealed class MidiRecorder : IDisposable
{
    private readonly record struct TimedMidiMessage(MidiMessage Message, TimeSpan Timestamp);

    private readonly MidiInputDevice _inputDevice;
    private readonly object _lock = new();

    private readonly ConcurrentQueue<TimedMidiMessage> _timedMessages = new();
    private readonly Stopwatch _stopwatch = new();
    private TimeSpan _currentLoopOffset = TimeSpan.Zero;
    private bool _isRecording;

    /// <summary>
    /// Gets a value indicating whether the recorder is currently active.
    /// </summary>
    public bool IsRecording => _isRecording;
    
    /// <summary>
    /// Gets the timeline start time of the current recording session.
    /// </summary>
    public TimeSpan StartTime { get; private set; }

    /// <summary>
    /// Occurs when recording is stopped, providing the recorder instance and the resulting MidiDataProvider.
    /// </summary>
    public event Action<MidiRecorder, MidiDataProvider>? RecordingStopped;

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiRecorder"/> class.
    /// </summary>
    /// <param name="inputDevice">The MIDI input device to record from.</param>
    public MidiRecorder(MidiInputDevice inputDevice)
    {
        _inputDevice = inputDevice;
    }

    /// <summary>
    /// Starts the recording process.
    /// </summary>
    public void StartRecording() => StartRecording(TimeSpan.Zero);
    
    internal void StartRecording(TimeSpan startTime)
    {
        lock (_lock)
        {
            if (_isRecording) return;
            StartTime = startTime;
            _currentLoopOffset = TimeSpan.Zero;
            _currentLoopOffset = TimeSpan.Zero;
            _timedMessages.Clear();
            _inputDevice.OnMessageReceived += OnMidiMessageReceived;
            _stopwatch.Restart();
            _isRecording = true;
        }
    }

    /// <summary>
    /// Stops the recording process and generates a MidiDataProvider from the captured data.
    /// </summary>
    /// <param name="timeToTickConverter">A delegate function that converts a TimeSpan into a MIDI tick value based on the project's tempo map.</param>
    /// <param name="ticksPerQuarterNote">The time division to use for the resulting MIDI file.</param>
    public void StopRecording(TimeToTickConverter timeToTickConverter, int ticksPerQuarterNote = 480)
    {
        lock (_lock)
        {
            if (!_isRecording) return;
            _stopwatch.Stop();
            _inputDevice.OnMessageReceived -= OnMidiMessageReceived;
            _isRecording = false;

            var provider = ProcessCapturedMessages(timeToTickConverter, ticksPerQuarterNote);
            RecordingStopped?.Invoke(this, provider);
        }
    }
    
    /// <summary>
    /// Adds a time offset, used for loop recording to correctly place notes on subsequent passes.
    /// </summary>
    /// <param name="offset">The duration of the loop.</param>
    internal void AddLoopOffset(TimeSpan offset)
    {
        lock (_lock)
        {
            _currentLoopOffset += offset;
        }
    }
    
    /// <summary>
    /// This method is a no-op kept for API compatibility. Timing is now handled by a high-resolution timer.
    /// </summary>
    /// <param name="samplesInBlock">The number of samples processed (unused).</param>
    public void UpdateSampleClock(int samplesInBlock) { }

    private void OnMidiMessageReceived(MidiMessage message, MidiDeviceInfo _)
    {
        if (!_isRecording) return;
        // Timestamp is the high-precision stopwatch time plus any accumulated loop offsets.
        _timedMessages.Enqueue(new TimedMidiMessage(message, _stopwatch.Elapsed + _currentLoopOffset));
    }

    private MidiDataProvider ProcessCapturedMessages(TimeToTickConverter timeToTickConverter, int ticksPerQuarterNote)
    {
        var messages = _timedMessages.OrderBy(m => m.Timestamp).ToList();
        var midiFile = new MidiFile { Format = 1, TicksPerQuarterNote = ticksPerQuarterNote };
        var midiTrack = new MidiTrack();

        if (messages.Count > 0)
        {
            long lastEventTick = 0;
            foreach (var timedMessage in messages)
            {
                var absoluteEventTime = StartTime + timedMessage.Timestamp;
                var absoluteTick = timeToTickConverter(absoluteEventTime);
                
                var deltaTicks = absoluteTick - lastEventTick;
                midiTrack.AddEvent(new ChannelEvent(deltaTicks, timedMessage.Message));
                lastEventTick = absoluteTick;
            }
        }
        
        midiTrack.AddEvent(new MetaEvent(0, MetaEventType.EndOfTrack, []));
        midiFile.AddTrack(midiTrack);

        return new MidiDataProvider(midiFile);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // If the recorder is disposed while still recording, it stops listening for messages
        // but does not finalize the captured data. The owner is responsible for calling StopRecording()
        // to retrieve the data before disposing.
        if (_isRecording)
        {
            _inputDevice.OnMessageReceived -= OnMidiMessageReceived;
            _isRecording = false;
        }
    }
}