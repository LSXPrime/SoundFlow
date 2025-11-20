using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Metadata.Midi;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Structs;
using SoundFlow.Providers;
using SoundFlow.Structs;
using SoundFlow.Synthesis.Interfaces;
using SoundFlow.Utils;

namespace SoundFlow.Synthesis;

/// <summary>
/// A component that plays back MIDI events from a MidiDataProvider in a sample-accurate manner
/// and dispatches them to a target IMidiControllable (e.g., a Synthesizer).
/// </summary>
public sealed class Sequencer : SoundComponent
{
    private readonly IMidiControllable _target;
    private long _currentTick;

    /// <inheritdoc />
    public override string Name { get; set; } = "Sequencer";
    
    /// <summary>
    /// Gets or sets the optional context for this sequencer. If provided, the sequencer will synchronize
    /// its timing to this external context (e.g., a composition's master clock). If null, it will use
    /// the MIDI data's own internal tempo map for playback.
    /// </summary>
    public ISequencerContext? Context { get; set; }

    /// <summary>
    /// Gets the MIDI data provider for this sequencer.
    /// </summary>
    public MidiDataProvider DataProvider { get; }

    /// <summary>
    /// Gets the current playback state of the sequencer.
    /// </summary>
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;

    /// <summary>
    /// Gets or sets whether the sequence should loop when it reaches the end.
    /// </summary>
    public bool IsLooping { get; set; }

    /// <summary>
    /// Gets the current playback time. The calculation uses the master context if available,
    /// otherwise it falls back to the data provider's internal tempo.
    /// </summary>
    public TimeSpan CurrentTime
    {
        get
        {
            // If a context is provided, use it for master timing.
            if (Context != null)
                return MidiTimeConverter.GetTimeSpanForTick(_currentTick, DataProvider.TicksPerQuarterNote, Context.TempoTrack);
            
            // Otherwise, use the data provider's internal timing (standalone mode).
            return DataProvider.GetTimeSpanForTick(_currentTick);
        }
    }
    
    /// <summary>
    /// Gets the duration of the sequence. The calculation uses the master context if available,
    /// otherwise it falls back to the data provider's internal tempo.
    /// </summary>
    public TimeSpan Duration
    {
        get
        {
            // If a context is provided, use it for master timing.
            if (Context != null)
                return MidiTimeConverter.GetTimeSpanForTick(DataProvider.LengthTicks, DataProvider.TicksPerQuarterNote, Context.TempoTrack);
            
            // Otherwise, use the data provider's internal timing (standalone mode).
            return DataProvider.Duration;
        }
    }


    /// <summary>
    /// Initializes a new instance of the <see cref="Sequencer"/> class.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The audio format, used for timing calculations.</param>
    /// <param name="dataProvider">The provider of MIDI data to be sequenced.</param>
    /// <param name="target">The MIDI controllable component that will receive the events.</param>
    public Sequencer(AudioEngine engine, AudioFormat format, MidiDataProvider dataProvider, IMidiControllable target) : base(engine, format)
    {
        DataProvider = dataProvider;
        _target = target;
        Enabled = false; // The sequencer is enabled/disabled via Play/Pause/Stop.
    }

    /// <summary>
    /// Starts or resumes playback of the sequence.
    /// </summary>
    public void Play()
    {
        State = PlaybackState.Playing;
        Enabled = true;
    }

    /// <summary>
    /// Pauses playback of the sequence.
    /// </summary>
    public void Pause()
    {
        State = PlaybackState.Paused;
        Enabled = false;
    }

    /// <summary>
    /// Stops playback and resets the position to the beginning.
    /// </summary>
    public void Stop()
    {
        State = PlaybackState.Stopped;
        Enabled = false;
        _currentTick = 0;
        // Send an "All Notes Off" to prevent stuck notes.
        _target.ProcessMidiMessage(new MidiMessage(0xB0, 123, 0));
    }

    /// <summary>
    /// Seeks to a specific time in the sequence.
    /// </summary>
    /// <param name="time">The time to seek to.</param>
    public void Seek(TimeSpan time)
    {
        // Send an "All Notes Off" before seeking to prevent stuck notes.
        _target.ProcessMidiMessage(new MidiMessage(0xB0, 123, 0));
        
        // Use the appropriate time conversion logic.
        _currentTick = Context != null 
            ? MidiTimeConverter.GetTickForTimeSpan(time, DataProvider.TicksPerQuarterNote, Context.TempoTrack) 
            : DataProvider.GetTickForTimeSpan(time);
    }

    /// <inheritdoc />
    /// <remarks>
    /// This method does not generate audio. It uses the audio render callback for sample-accurate MIDI event timing.
    /// </remarks>
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        if (State != PlaybackState.Playing)
        {
            return; // Don't process events if not playing.
        }

        var framesInBlock = buffer.Length / channels;
        var blockDuration = TimeSpan.FromSeconds((double)framesInBlock / Format.SampleRate);
        var endTime = CurrentTime + blockDuration;

        var startTick = _currentTick;
        var endTick = Context != null 
            ? MidiTimeConverter.GetTickForTimeSpan(endTime, DataProvider.TicksPerQuarterNote, Context.TempoTrack)
            : DataProvider.GetTickForTimeSpan(endTime);

        var eventsToPlay = DataProvider.GetEvents(startTick, endTick);
        foreach (var timedEvent in eventsToPlay)
        {
            if (timedEvent.Event is ChannelEvent channelEvent)
            {
                _target.ProcessMidiMessage(channelEvent.Message);
            }
            // TODO: Meta and SysEx events are ignored for now.
        }

        _currentTick = endTick;

        // Handle looping
        if (IsLooping && _currentTick >= DataProvider.LengthTicks)
        {
            _currentTick %= DataProvider.LengthTicks;
            // Send an "All Notes Off" to prevent stuck notes when looping.
            _target.ProcessMidiMessage(new MidiMessage(0xB0, 123, 0));
        }
        else if (!IsLooping && _currentTick >= DataProvider.LengthTicks)
        {
            Stop();
        }
    }
}