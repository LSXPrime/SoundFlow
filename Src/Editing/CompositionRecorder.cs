using SoundFlow.Metadata.Midi;
using SoundFlow.Midi;
using SoundFlow.Midi.Devices;
using SoundFlow.Midi.Enums;
using SoundFlow.Providers;
using SoundFlow.Utils;

namespace SoundFlow.Editing;

/// <summary>
/// Defines the behavior of the MIDI recorder when recording starts.
/// </summary>
public enum RecordingMode
{
    /// <summary>
    /// Always creates a new MIDI segment for the recording.
    /// </summary>
    Normal,
    /// <summary>
    /// Merges the newly recorded notes into an existing MIDI segment. If no segment is at the start time, creates a new one.
    /// </summary>
    OverdubMerge
}

/// <summary>
/// Manages the MIDI recording workflow for a <see cref="Composition"/>.
/// This class handles arming tracks, starting/stopping recording, and updating the sample clock.
/// </summary>
public sealed class CompositionRecorder : IDisposable
{
    private readonly Composition _composition;
    private bool _isWaitingForPunchIn;

    private sealed class ArmedTrackState(MidiTrack track, MidiRecorder recorder)
    {
        public readonly MidiTrack Track = track;
        public readonly MidiRecorder Recorder = recorder;
        public RecordingMode Mode;
        public MidiSegment? TargetSegment;
        public long LoopSampleOffset;
    }

    private readonly Dictionary<MidiTrack, ArmedTrackState> _armedTracks = new();

    /// <summary>
    /// Gets or sets the time at which recording should automatically start. If null, recording starts immediately when <see cref="StartRecording"/> is called.
    /// </summary>
    public TimeSpan? PunchInTime { get; set; }

    /// <summary>
    /// Gets or sets the time at which recording should automatically stop. If null, recording continues until manually stopped.
    /// </summary>
    public TimeSpan? PunchOutTime { get; set; }

    /// <summary>
    /// Gets a value indicating whether any armed tracks are currently recording.
    /// </summary>
    public bool IsRecording => _armedTracks.Values.Any(s => s.Recorder.IsRecording);

    /// <summary>
    /// Gets a value indicating whether the recorder is armed and waiting for the punch-in time to be reached.
    /// </summary>
    public bool IsWaitingForPunchIn => _isWaitingForPunchIn;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionRecorder"/> class.
    /// </summary>
    /// <param name="composition">The composition instance that will be recorded into.</param>
    internal CompositionRecorder(Composition composition)
    {
        _composition = composition;
    }
    
    /// <summary>
    /// Arms a MIDI track for recording from a specific input device.
    /// </summary>
    /// <param name="track">The MIDI track to arm.</param>
    /// <param name="inputDevice">The MIDI input device to record from.</param>
    public void ArmTrackForRecording(MidiTrack track, MidiInputDevice inputDevice)
    {
        if (_armedTracks.TryGetValue(track, out var existingState))
        {
            existingState.Recorder.Dispose();
        }
        var recorder = new MidiRecorder(inputDevice);
        _armedTracks[track] = new ArmedTrackState(track, recorder);
    }
    
    /// <summary>
    /// Disarms a MIDI track, stopping any active recording on it.
    /// </summary>
    /// <param name="track">The MIDI track to disarm.</param>
    public void DisarmTrack(MidiTrack track)
    {
        if (!_armedTracks.TryGetValue(track, out var state)) return;
        state.Recorder.Dispose();
        _armedTracks.Remove(track);
    }
    
    /// <summary>
    /// Pre-arms the recorder. If a <see cref="PunchInTime"/> is set, it will wait until that time to start. Otherwise, it starts immediately.
    /// </summary>
    /// <param name="startTime">The time on the composition timeline where recording begins if not using punch-in.</param>
    /// <param name="mode">The recording mode to use (e.g., create new segment or overdub).</param>
    /// <param name="targetSegment">For overdub/merge mode, the segment to record into. Ignored for normal mode.</param>
    public void StartRecording(TimeSpan startTime, RecordingMode mode = RecordingMode.Normal, MidiSegment? targetSegment = null)
    {
        foreach (var state in _armedTracks.Values)
        {
            state.Mode = mode;
            state.TargetSegment = targetSegment;
            state.LoopSampleOffset = 0; // Reset loop offset at the start of each recording.

            // Detach any previous handler to avoid duplicates & Attach it only for the current session.
            state.Recorder.RecordingStopped -= OnRecordingStopped; 
            state.Recorder.RecordingStopped += OnRecordingStopped; 
        }

        if (PunchInTime > startTime)
            _isWaitingForPunchIn = true;
        else
            StartInternalRecorders(startTime);
    }

    /// <summary>
    /// Immediately starts all armed recorders. For internal use by the transport.
    /// </summary>
    /// <param name="startTime">The exact start time.</param>
    internal void StartInternalRecorders(TimeSpan startTime)
    {
        _isWaitingForPunchIn = false;
        foreach (var state in _armedTracks.Values)
        {
            state.Recorder.StartRecording(startTime);
        }
    }

    private void OnRecordingStopped(MidiRecorder recorder, MidiDataProvider provider)
    {
        var armedState = _armedTracks.Values.FirstOrDefault(s => s.Recorder == recorder);
        if (armedState == null) return;
        
        // Find the start time for the new segment.
        var startTime = armedState.Recorder.StartTime;

        if (armedState is { Mode: RecordingMode.OverdubMerge, TargetSegment: not null })
        {
            // Merge newly recorded events into the existing target segment.
            var target = armedState.TargetSegment;
            var offsetTicks = MidiTimeConverter.GetTickForTimeSpan(startTime - target.TimelineStartTime, target.Sequence.TicksPerQuarterNote, _composition.TempoTrack);
            var openNotes = new Dictionary<(int channel, int pitch), (long startTick, int velocity)>();

            foreach (var timedEvent in provider.Events)
            {
                if (timedEvent.Event is not ChannelEvent ce) continue;

                var absoluteTickWithOffset = offsetTicks + timedEvent.AbsoluteTimeTicks;
                var key = (ce.Message.Channel, ce.Message.NoteNumber);

                switch (ce.Message.Command)
                {
                    case MidiCommand.NoteOn when ce.Message.Velocity > 0:
                        openNotes[key] = (timedEvent.AbsoluteTimeTicks, ce.Message.Velocity);
                        break;
                    
                    case MidiCommand.NoteOff:
                    case MidiCommand.NoteOn when ce.Message.Velocity == 0:
                        if (openNotes.Remove(key, out var noteStart))
                        {
                            var durationTicks = timedEvent.AbsoluteTimeTicks - noteStart.startTick;
                            if (durationTicks > 0)
                            {
                                target.Sequence.AddNote(offsetTicks + noteStart.startTick, durationTicks, key.NoteNumber, noteStart.velocity);
                            }
                        }
                        break;
                        
                    case MidiCommand.ControlChange:
                        target.Sequence.AddControlPoint(ce.Message.ControllerNumber, absoluteTickWithOffset, ce.Message.ControllerValue);
                        break;
                        
                    case MidiCommand.PitchBend:
                        target.Sequence.AddControlPoint(-1, absoluteTickWithOffset, ce.Message.PitchBendValue);
                        break;
                }
            }
            target.MarkDirty();
        }
        else
        {
            // In the Normal mode, create a new segment.
            var sequence = new MidiSequence(provider.ToMidiFile());
            var segment = new MidiSegment(sequence, startTime, $"Rec {DateTime.Now:HH-mm-ss}");
            armedState.Track.AddSegment(segment);
        }
    }

    /// <summary>
    /// Stops recording on all currently active MIDI recorders.
    /// </summary>
    public void StopRecording()
    {
        _isWaitingForPunchIn = false;
        foreach (var state in _armedTracks.Values)
        {
            if (!state.Recorder.IsRecording) continue;

            state.Recorder.StopRecording(
                time => MidiTimeConverter.GetTickForTimeSpan(time, _composition.TicksPerQuarterNote, _composition.TempoTrack),
                _composition.TicksPerQuarterNote
            );
        }
    }

    /// <summary>
    /// Updates the internal sample clock for all active recorders. This method must be called
    /// from a synchronized audio context, such as the composition's render loop.
    /// </summary>
    /// <param name="samplesInBlock">The number of samples processed in the last audio block.</param>
    internal void UpdateSampleClock(int samplesInBlock)
    {
        foreach (var state in _armedTracks.Values)
        {
            state.Recorder.UpdateSampleClock(samplesInBlock);
        }
    }

    /// <summary>
    /// Notifies active recorders that a transport loop has occurred.
    /// This is used to correctly timestamp notes in loop recording sessions.
    /// </summary>
    /// <param name="loopDurationSamples">The duration of the loop in audio samples.</param>
    internal void OnTransportLoop(long loopDurationSamples)
    {
        // Convert the sample duration to a TimeSpan.
        var loopDuration = TimeSpan.FromSeconds((double)loopDurationSamples / _composition.SampleRate / _composition.TargetChannels);

        foreach (var state in _armedTracks.Values)
        {
            if (state.Recorder.IsRecording)
            {
                state.Recorder.AddLoopOffset(loopDuration);
            }
        }
    }

    /// <summary>
    /// Disposes all active MIDI recorders, effectively disarming all tracks.
    /// </summary>
    public void Dispose()
    {
        foreach (var state in _armedTracks.Values)
        {
            state.Recorder.RecordingStopped -= OnRecordingStopped;
            state.Recorder.Dispose();
        }
        _armedTracks.Clear();
    }
}