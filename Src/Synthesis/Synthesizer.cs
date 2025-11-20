using System.Buffers;
using SoundFlow.Abstracts;
using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Routing;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;
using SoundFlow.Synthesis.Interfaces;
using SoundFlow.Synthesis.Voices;

namespace SoundFlow.Synthesis;

/// <summary>
/// A polyphonic, multi-timbral synthesizer component that generates audio from MIDI messages.
/// Supports internal effects chain and automatic processing of temporal modifiers (like Arpeggiators).
/// </summary>
public sealed class Synthesizer : SoundComponent, IMidiControllable
{
    private readonly MidiChannel[] _channels = new MidiChannel[16];
    private readonly Dictionary<int, IVoice> _mpeNoteToVoiceMap = new(); // Note number -> Active MPE voice
    private bool _mpeEnabled;
    
    private readonly List<MidiModifier> _midiModifiers = [];
    private readonly object _modifierLock = new();
    
    private float _bpm = 120f;

    /// <inheritdoc />
    public override string Name { get; set; } = "Synthesizer";

    /// <summary>
    /// Gets a read-only list of MIDI modifiers (effects) applied to incoming messages before they trigger voices.
    /// </summary>
    public IReadOnlyList<MidiModifier> MidiModifiers
    {
        get
        {
            lock (_modifierLock)
            {
                return _midiModifiers.AsReadOnly();
            }
        }
    }

    /// <summary>
    /// Gets or sets the Tempo (Beats Per Minute) used to drive temporal modifiers like Arpeggiators.
    /// Default is 120.
    /// </summary>
    public float Bpm
    {
        get => _bpm;
        set => _bpm = Math.Max(0.1f, value);
    }
    
    /// <summary>
    /// Gets or sets whether the synthesizer operates in MPE (MIDI Polyphonic Expression) mode.
    /// </summary>
    public bool MpeEnabled
    {
        get => _mpeEnabled;
        set
        {
            if (_mpeEnabled == value) return;
            _mpeEnabled = value;
            
            // When switching modes, send an "All Notes Off" to prevent stuck notes.
            DispatchMidiMessage(new MidiMessage(0xB0, 123, 0));
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Synthesizer"/> class.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The audio format for the synthesizer's output.</param>
    /// <param name="instrumentBank">The bank of instruments the synthesizer will use.</param>
    public Synthesizer(AudioEngine engine, AudioFormat format, IInstrumentBank instrumentBank) : base(engine, format)
    {
        for (var i = 0; i < _channels.Length; i++)
        {
            _channels[i] = new MidiChannel(format, instrumentBank);
        }
    }

    /// <summary>
    /// Adds a <see cref="MidiModifier"/> to the end of the synthesizer's MIDI processing chain.
    /// </summary>
    /// <param name="modifier">The modifier to add.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="modifier"/> is null.</exception>
    public void AddMidiModifier(MidiModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        lock (_modifierLock)
        {
            if (!_midiModifiers.Contains(modifier))
                _midiModifiers.Add(modifier);
        }
    }

    /// <summary>
    /// Removes a <see cref="MidiModifier"/> from the synthesizer's MIDI processing chain.
    /// </summary>
    /// <param name="modifier">The modifier to remove.</param>
    public void RemoveMidiModifier(MidiModifier modifier)
    {
        lock (_modifierLock)
        {
            _midiModifiers.Remove(modifier);
        }
    }

    /// <inheritdoc />
    public void ProcessMidiMessage(MidiMessage message)
    {
        // 1. Create a list for the pipeline
        List<MidiMessage> currentMessages = [message];

        // 2. Run through Modifier Pipeline
        lock (_modifierLock)
        {
            foreach (var modifier in _midiModifiers)
            {
                if (!modifier.IsEnabled) continue;

                var nextStageMessages = new List<MidiMessage>();
                foreach (var inputMsg in currentMessages)
                {
                    nextStageMessages.AddRange(modifier.Process(inputMsg));
                }
                currentMessages = nextStageMessages;
                
                // If everything was filtered out, stop early
                if (currentMessages.Count == 0) break;
            }
        }

        // 3. Dispatch final messages to internal sound engine
        foreach (var finalMessage in currentMessages)
        {
            DispatchMidiMessage(finalMessage);
        }
    }

    /// <summary>
    /// Internal method to route a MIDI message to the appropriate channel or MPE logic.
    /// This bypasses the modifier pipeline to prevent infinite loops from internal generators.
    /// </summary>
    private void DispatchMidiMessage(MidiMessage message)
    {
        var channelIndex = message.Channel - 1;
        if (channelIndex < 0 || channelIndex >= _channels.Length) return;

        if (MpeEnabled)
        {
            switch (message.Command)
            {
                case MidiCommand.NoteOn when message.Velocity > 0:
                    var voice = _channels[channelIndex].NoteOn(message.NoteNumber, message.Velocity);
                    _mpeNoteToVoiceMap[message.NoteNumber] = voice;
                    break;
                case MidiCommand.NoteOff:
                case MidiCommand.NoteOn when message.Velocity == 0:
                    _channels[channelIndex].NoteOff(message.NoteNumber);
                    _mpeNoteToVoiceMap.Remove(message.NoteNumber);
                    break;
                default:
                    // Global messages (like sustain pedal) on the master channel are handled normally
                    _channels[channelIndex].ProcessMidiMessage(message);
                    break;
            }
        }
        else
        {
            // Standard multi-timbral behavior
            _channels[channelIndex].ProcessMidiMessage(message);
        }
    }
    
    /// <summary>
    /// Resets the synthesizer to a clean state, killing all sounding voices and resetting all channel parameters.
    /// </summary>
    public void Reset()
    {
        foreach (var channel in _channels)
        {
            channel.Reset();
        }
        _mpeNoteToVoiceMap.Clear();
    }

    /// <summary>
    /// For internal use by MidiManager. Processes a high-level MPE event.
    /// </summary>
    /// <param name="mpeEvent">The MPE event object.</param>
    internal void ProcessMpeEvent(object mpeEvent)
    {
        if (!MpeEnabled) return;

        switch (mpeEvent)
        {
            case MidiMessage msg:
                // Route MPE-derived Note On/Off through the standard pipeline 
                // so modifiers like Velocity or Transpose can still apply.
                ProcessMidiMessage(msg);
                break;
            case MidiManager.GlobalPitchBendEvent gpb:
                // Global pitch bend, typically +/- 2 semitones.
                var bendSemitones = (gpb.PitchBendValue - 8192) / 8192.0f * 2.0f;
                foreach (var voice in _mpeNoteToVoiceMap.Values)
                {
                    // The 'channelPitchBend' parameter is used for global/channel-wide bend.
                    voice.ProcessMidiControl(default, bendSemitones);
                }
                break;
            case MidiManager.PerNotePitchBendEvent pb:
                if (_mpeNoteToVoiceMap.TryGetValue(pb.NoteNumber, out var pbVoice)) 
                    pbVoice.SetPerNotePitchBend(pb.BendSemitones);
                break;
            case MidiManager.PerNotePressureEvent p:
                if (_mpeNoteToVoiceMap.TryGetValue(p.NoteNumber, out var pVoice)) 
                    pVoice.SetPerNotePressure(p.Pressure);
                break;
            case MidiManager.PerNoteTimbreEvent t:
                if (_mpeNoteToVoiceMap.TryGetValue(t.NoteNumber, out var tVoice)) 
                    tVoice.SetPerNoteTimbre(t.Timbre);
                break;
        }
    }

    /// <inheritdoc />
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        // 1. Handle Temporal Modifiers (e.g. Arpeggiators)
        // Calculate how much time this buffer represents
        if (Format.SampleRate > 0 && buffer.Length > 0 && channels > 0)
        {
            double bufferDurationSeconds = (double)buffer.Length / channels / Format.SampleRate;
            List<MidiMessage> generatedMessages = [];

            lock (_modifierLock)
            {
                foreach (var modifier in _midiModifiers)
                {
                    if (modifier.IsEnabled && modifier is ITemporalMidiModifier temporalMod)
                    {
                        generatedMessages.AddRange(temporalMod.Tick(bufferDurationSeconds, Bpm));
                    }
                }
            }

            // Dispatch any events generated by the modifiers (e.g. Arp notes) directly to sound generation
            foreach (var msg in generatedMessages)
            {
                DispatchMidiMessage(msg);
            }
        }

        // 2. Generate Audio
        buffer.Clear();
        float[]? rentedBuffer = null;

        try
        {
            rentedBuffer = ArrayPool<float>.Shared.Rent(buffer.Length);
            var tempBuffer = rentedBuffer.AsSpan(0, buffer.Length);

            foreach (var channel in _channels)
            {
                channel.Render(tempBuffer);

                // Mix the channel's output into the main buffer
                for (var i = 0; i < buffer.Length; i++)
                {
                    buffer[i] += tempBuffer[i];
                }
            }
        }
        finally
        {
            if (rentedBuffer != null) 
                ArrayPool<float>.Shared.Return(rentedBuffer);
        }
    }
}