using System.Buffers;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;
using SoundFlow.Synthesis.Instruments;
using SoundFlow.Synthesis.Interfaces;

namespace SoundFlow.Synthesis.Voices;

/// <summary>
/// Manages the state and voices for a single MIDI channel.
/// </summary>
internal sealed class MidiChannel
{
    private const int MaxPolyphony = 64;
    private readonly List<IVoice> _activeVoices = [];
    private readonly object _voiceLock = new();

    private readonly IInstrumentBank _instrumentBank;
    private readonly AudioFormat _format;
    private Instrument _currentInstrument;
    private float _volume = 1.0f;
    private float _pan = 0.5f;
    private float _pitchBend; // In semitones
    private bool _damperPedalOn;
    private int _currentBankMsb = -1;
    private int _currentBankLsb = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiChannel"/> class.
    /// </summary>
    /// <param name="format">The audio format.</param>
    /// <param name="instrumentBank">The instrument bank to source sounds from.</param>
    public MidiChannel(AudioFormat format, IInstrumentBank instrumentBank)
    {
        _format = format;
        _instrumentBank = instrumentBank;
        _currentInstrument = _instrumentBank.GetInstrument(0, 0);
    }

    /// <summary>
    /// Processes an incoming MIDI message for this channel.
    /// </summary>
    public void ProcessMidiMessage(MidiMessage message)
    {
        switch (message.Command)
        {
            case MidiCommand.NoteOn when message.Velocity > 0:
                NoteOn(message.NoteNumber, message.Velocity);
                break;
            case MidiCommand.NoteOff:
            case MidiCommand.NoteOn when message.Velocity == 0:
                NoteOff(message.NoteNumber);
                break;
            case MidiCommand.ControlChange:
                HandleControlChange(message.ControllerNumber, message.ControllerValue);
                break;
            case MidiCommand.ProgramChange:
                var msb = _currentBankMsb != -1 ? _currentBankMsb : 0;
                var lsb = _currentBankLsb != -1 ? _currentBankLsb : 0;
                var bank = (msb * 128) + lsb;
    
                _currentInstrument = _instrumentBank.GetInstrument(bank, message.Data1);
                break;
            case MidiCommand.PitchBend:
                // Pitch bend is 14-bit, centered at 8192. Range is typically +/- 2 semitones.
                _pitchBend = (message.PitchBendValue - 8192) / 8192.0f * 2.0f;
                // Propagate pitch bend to all active voices
                lock (_voiceLock)
                {
                    foreach (var voice in _activeVoices)
                    {
                        voice.ProcessMidiControl(message, _pitchBend);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Renders all active voices for this channel into the provided buffer.
    /// </summary>
    public void Render(Span<float> buffer)
    {
        buffer.Clear();
        lock (_voiceLock)
        {
            // First, remove any voices that have finished their lifecycle.
            _activeVoices.RemoveAll(v => v.IsFinished);
            
            if (_activeVoices.Count == 0)
            {
                return;
            }

            var tempBuffer = ArrayPool<float>.Shared.Rent(buffer.Length);
            try
            {
                var tempSpan = tempBuffer.AsSpan(0, buffer.Length);
                foreach (var voice in _activeVoices)
                {
                    tempSpan.Clear();
                    voice.Render(tempSpan);
                    for(var i = 0; i < buffer.Length; i++)
                    {
                        buffer[i] += tempSpan[i];
                    }
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(tempBuffer);
            }
        }

        // Apply channel volume and pan
        for (var i = 0; i < buffer.Length; i++)
        {
            var sample = buffer[i] * _volume;
            if (_format.Channels == 2)
            {
                var panAngle = _pan * MathF.PI / 2.0f;
                sample *= i % 2 == 0 ? MathF.Cos(panAngle) : MathF.Sin(panAngle);
            }
            buffer[i] = sample;
        }
    }
    
    /// <summary>
    /// Resets the channel to its default state, killing all active voices.
    /// </summary>
    public void Reset()
    {
        lock (_voiceLock)
        {
            foreach (var voice in _activeVoices)
            {
                voice.Kill();
            }
            _activeVoices.Clear();
        }
        
        // Reset channel state to defaults
        _volume = 1.0f;
        _pan = 0.5f;
        _pitchBend = 0.0f;
        _damperPedalOn = false;
        _currentBankMsb = -1;
        _currentBankLsb = -1;
        _currentInstrument = _instrumentBank.GetInstrument(0, 0);
    }

    internal IVoice NoteOn(int noteNumber, int velocity)
    {
        lock (_voiceLock)
        {
            if (_activeVoices.Count >= MaxPolyphony)
            {
                // Voice stealing: find the oldest, non-releasing voice and kill it.
                var voiceToSteal = _activeVoices.FirstOrDefault(v => !v.IsReleasing);
                if (voiceToSteal != null)
                    voiceToSteal.Kill();
                else if(_activeVoices.Count > 0) 
                    _activeVoices[0].Kill(); // If all are releasing, kill the oldest one anyway
                
                // Clean up any voices that might have finished due to the kill command
                _activeVoices.RemoveAll(v => v.IsFinished);
            }

            var voiceDef = _currentInstrument.GetVoiceDefinition(noteNumber, velocity);
            var baseFrequency = 440.0f * MathF.Pow(2.0f, (noteNumber - 69.0f) / 12.0f);
            var context = new VoiceContext
            {
                NoteNumber = noteNumber,
                Velocity = velocity,
                BaseFrequency = baseFrequency,
                Frequency = baseFrequency,
                ChannelPitchBend = _pitchBend,
                SampleRate = _format.SampleRate
            };

            var newVoice = voiceDef.CreateVoice(context);
            _activeVoices.Add(newVoice);
            return newVoice;
        }
    }

    internal void NoteOff(int noteNumber)
    {
        lock (_voiceLock)
        {
            foreach (var voice in _activeVoices)
            {
                if (voice.NoteNumber != noteNumber || voice.IsReleasing) continue;
                if (_damperPedalOn)
                    voice.IsSustained = true; // Don't release the note yet, but mark it as sustained.
                else
                    voice.NoteOff();
            }
        }
    }

    private void HandleControlChange(int controller, int value)
    {
        switch (controller)
        {
            case 0: // Bank Select MSB
                _currentBankMsb = value;
                break;
            case 7: // Main Volume
                _volume = value / 127.0f;
                break;
            case 10: // Pan
                _pan = value / 127.0f;
                break;
            case 32: // Bank Select LSB
                _currentBankLsb = value;
                break;
            case 64: // Damper Pedal (Sustain)
                var pedalIsOn = value >= 64;
                if (_damperPedalOn && !pedalIsOn)
                {
                    // Pedal was released. Find all voices that were being sustained and release them now.
                    lock (_voiceLock)
                    {
                        foreach (var voice in _activeVoices)
                        {
                            if (!voice.IsSustained) continue;
                            voice.NoteOff();
                            voice.IsSustained = false;
                        }
                    }
                }
                _damperPedalOn = pedalIsOn;
                break;
                
            case 123: // All Notes Off
                lock (_voiceLock)
                {
                    foreach (var voice in _activeVoices)
                    {
                        voice.NoteOff();
                    }
                }
                break;
        }
    }
}