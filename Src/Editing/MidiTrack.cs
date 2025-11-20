using SoundFlow.Abstracts;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Midi;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Routing.Nodes;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Editing;

/// <summary>
/// Represents a MIDI track within a composition, containing a collection of MIDI segments
/// and routing their output to a specific MIDI-controllable target.
/// </summary>
public class MidiTrack
{
    private string _name;
    private IMidiDestinationNode? _target;
    private TrackSettings _settings;
    private Composition? _parentComposition;

    /// <summary>
    /// Gets or sets the name of the track.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets the list of <see cref="MidiSegment"/>s contained within this track.
    /// </summary>
    public List<MidiSegment> Segments { get; } = [];

    /// <summary>
    /// Gets or sets the target node (e.g., a Synthesizer or a physical MIDI output) that will receive MIDI events from this track.
    /// </summary>
    public IMidiDestinationNode? Target
    {
        get => _target;
        set
        {
            if (_target == value) return;
            _target = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the settings applied to this track. While not all settings (like Volume/Pan) directly
    /// apply to MIDI data, Mute/Solo/IsEnabled and MIDI modifiers are relevant.
    /// </summary>
    public TrackSettings Settings
    {
        get => _settings;
        set
        {
            if (_settings == value) return;
            _settings = value ?? throw new ArgumentNullException(nameof(value));
            _settings.ParentTrack = null; // MIDI tracks don't have the same parentage concept for audio
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the parent composition to which this track belongs.
    /// </summary>
    public Composition? ParentComposition
    {
        get => _parentComposition;
        set => _parentComposition = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiTrack"/> class.
    /// </summary>
    /// <param name="name">The name of the track.</param>
    /// <param name="target">The initial target for MIDI events.</param>
    /// <param name="settings">Optional track settings.</param>
    public MidiTrack(string name = "MIDI Track", IMidiDestinationNode? target = null, TrackSettings? settings = null)
    {
        _name = name;
        _target = target;
        _settings = settings ?? new TrackSettings();
    }

    /// <summary>
    /// Renders the MIDI output for this track for a given time range by driving its target
    /// synthesizer to produce audio in a sample-accurate manner.
    /// </summary>
    /// <param name="startTime">The global timeline start time for rendering.</param>
    /// <param name="duration">The duration of audio to render.</param>
    /// <param name="outputBuffer">The buffer to fill with rendered audio. This method mixes its output into the buffer.</param>
    public void Render(TimeSpan startTime, TimeSpan duration, Span<float> outputBuffer)
    {
        if (!Settings.IsEnabled || Settings.IsMuted || ParentComposition == null ||
            Target is not MidiTargetNode { Target: SoundComponent targetComponent })
        {
            return;
        }

        var ticksPerQuarterNote = ParentComposition.TicksPerQuarterNote;
        var tempoTrack = ParentComposition.TempoTrack;

        var startTick = MidiTimeConverter.GetTickForTimeSpan(startTime, ticksPerQuarterNote, tempoTrack);
        var endTick = MidiTimeConverter.GetTickForTimeSpan(startTime + duration, ticksPerQuarterNote, tempoTrack);

        var eventsToProcess = Segments
            .SelectMany(seg =>
            {
                var segmentStartTick = MidiTimeConverter.GetTickForTimeSpan(seg.TimelineStartTime, ticksPerQuarterNote, tempoTrack);
                return seg.DataProvider.Events.Select(e => (AbsoluteTick: e.AbsoluteTimeTicks + segmentStartTick, e.Event));
            })
            .Where(e => e.AbsoluteTick >= startTick && e.AbsoluteTick < endTick)
            .OrderBy(e => e.AbsoluteTick)
            .ToList();

        var lastRenderedTick = startTick;
        var samplesRendered = 0;

        foreach (var (absoluteTick, midiEvent) in eventsToProcess)
        {
            // 1. Render audio from the last event up to the current one.
            var timeOfThisEvent = MidiTimeConverter.GetTimeSpanForTick(absoluteTick, ticksPerQuarterNote, tempoTrack);
            var timeOfLastEvent = MidiTimeConverter.GetTimeSpanForTick(lastRenderedTick, ticksPerQuarterNote, tempoTrack);
            var timeToRender = timeOfThisEvent - timeOfLastEvent;

            if (timeToRender > TimeSpan.Zero)
            {
                var samplesToRender = (int)(timeToRender.TotalSeconds * ParentComposition.SampleRate * ParentComposition.TargetChannels);
                if (samplesRendered + samplesToRender > outputBuffer.Length)
                {
                    samplesToRender = outputBuffer.Length - samplesRendered;
                }

                if (samplesToRender > 0)
                {
                    targetComponent.Process(outputBuffer.Slice(samplesRendered, samplesToRender), ParentComposition.TargetChannels);
                    samplesRendered += samplesToRender;
                }
            }

            // 2. Process the current MIDI event.
            switch (midiEvent)
            {
                case ChannelEvent channelEvent:
                    var messagesToProcess = new List<MidiMessage> { channelEvent.Message };
                    
                    foreach (var modifier in Settings.MidiModifiers)
                    {
                        if (!modifier.IsEnabled) continue;
                        
                        var nextMessages = new List<MidiMessage>();
                        foreach (var msg in messagesToProcess)
                        {
                            nextMessages.AddRange(modifier.Process(msg));
                        }
                        messagesToProcess = nextMessages;
                    }

                    foreach (var finalMessage in messagesToProcess)
                    {
                        Target.ProcessMessage(finalMessage);
                    }
                    break;
                case SysExEvent sysExEvent:
                    // SysEx messages bypass the modifier chain and are only sent to physical output devices.
                    if (Target is MidiOutputNode { Device.IsDisposed: false } outputNode) 
                        outputNode.Device.SendSysEx(sysExEvent.Data);
                    
                    break;
            }

            lastRenderedTick = absoluteTick;
        }

        // 3. Render any remaining audio from the last event to the end of the buffer.
        var remainingSamples = outputBuffer.Length - samplesRendered;
        if (remainingSamples > 0)
        {
            targetComponent.Process(outputBuffer.Slice(samplesRendered, remainingSamples), ParentComposition.TargetChannels);
        }
    }
    
    /// <summary>
    /// Adds a <see cref="MidiSegment"/> to the track and re-sorts the segments by time.
    /// </summary>
    /// <param name="segment">The MIDI segment to add.</param>
    public void AddSegment(MidiSegment segment)
    {
        segment.ParentTrack = this;
        Segments.Add(segment);
        Segments.Sort((a, b) => a.TimelineStartTime.CompareTo(b.TimelineStartTime));
        MarkDirty();
    }
    
    /// <summary>
    /// Removes a <see cref="MidiSegment"/> from the track.
    /// </summary>
    /// <param name="segment">The MIDI segment to remove.</param>
    /// <returns>True if the segment was successfully removed, false otherwise.</returns>
    public bool RemoveSegment(MidiSegment segment)
    {
        segment.ParentTrack = null;
        var removed = Segments.Remove(segment);
        if(removed) MarkDirty();
        return removed;
    }
    
    /// <summary>
    /// Calculates the total duration of the track based on the latest ending MIDI segment.
    /// </summary>
    /// <returns>A <see cref="TimeSpan"/> representing the total duration of the track.</returns>
    public TimeSpan CalculateDuration()
    {
        return Segments.Count == 0 ? TimeSpan.Zero : Segments.Max(s => s.TimelineEndTime);
    }

    /// <summary>
    /// Marks the parent composition as dirty (having unsaved changes).
    /// </summary>
    public void MarkDirty()
    {
        ParentComposition?.MarkDirty();
    }
}