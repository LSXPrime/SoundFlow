using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Structs;

namespace SoundFlow.Midi.Modifier;

/// <summary>
/// Defines the playback direction of the arpeggiator.
/// </summary>
public enum ArpMode
{
    /// <summary>
    /// Ascends through the held notes, playing them in order of lowest to highest.
    /// </summary>
    Up,
    /// <summary>
    /// Descends through the held notes, playing them in order of highest to lowest.
    /// </summary>
    Down,
    /// <summary>
    /// Ascends then descends through the held notes, without repeating top/bottom notes.
    /// </summary>
    UpDown, 
    /// <summary>
    /// Plays the held notes in a random order.
    /// </summary>
    Random
}

/// <summary>
/// A stateful MIDI modifier that creates rhythmic patterns from held notes.
/// This modifier implements <see cref="ITemporalMidiModifier"/> allowing it to be driven automatically
/// by a host Synthesizer's audio clock.
/// </summary>
public sealed class ArpeggiatorModifier : MidiModifier, ITemporalMidiModifier
{
    /// <inheritdoc />
    public override string Name => $"Arpeggiator ({Mode})";

    #region Properties
    
    /// <summary>
    /// Gets or sets the arpeggiation pattern.
    /// </summary>
    public ArpMode Mode { get; set; } = ArpMode.Up;

    /// <summary>
    /// Gets or sets the number of octaves the pattern will span.
    /// </summary>
    public int Octaves { get; set; } = 1;
    
    /// <summary>
    /// Gets or sets the step duration in Beats (Quarter Notes).
    /// <para>1.0 = Quarter Note, 0.5 = Eighth Note, 0.25 = Sixteenth Note.</para>
    /// Default is 0.25 (1/16th).
    /// </summary>
    public double Rate { get; set; } = 0.25;

    /// <summary>
    /// Gets or sets the gate length (note duration) as a fraction of the step rate (0.0 to 1.0).
    /// </summary>
    public double Gate { get; set; } = 0.9;

    #endregion

    private readonly List<int> _heldNotes = [];
    private readonly object _lock = new();
    
    private int _arpIndex = -1;
    private int _arpOctave;
    private int _arpDirection = 1; // 1 for up, -1 for down
    
    private int _lastNotePlayed = -1;
    private int _lastVelocityPlayed;
    
    private double _accumulatedTime;
    private static readonly Random Random = new();

    /// <summary>
    /// Processes incoming MIDI messages to manage the list of held notes.
    /// Note On messages are consumed to build the chord and do not pass through.
    /// </summary>
    public override IEnumerable<MidiMessage> Process(MidiMessage message)
    {
        lock (_lock)
        {
            if (message.Command is MidiCommand.NoteOn && message.Velocity > 0)
            {
                // Add note and restart arp if it was previously empty.
                if (!_heldNotes.Contains(message.NoteNumber))
                {
                    bool wasEmpty = _heldNotes.Count == 0;
                    _heldNotes.Add(message.NoteNumber);
                    _heldNotes.Sort();
                    
                    if (wasEmpty)
                    {
                        ResetState();
                        // Force immediate trigger on next tick.
                        _accumulatedTime = double.MaxValue; 
                    }
                }
                _lastVelocityPlayed = message.Velocity;
            }
            else if (message.Command is MidiCommand.NoteOff || message is { Command: MidiCommand.NoteOn, Velocity: 0 })
            {
                _heldNotes.Remove(message.NoteNumber);
            }
            else
            {
                // Pass through all other message types (CC, PitchBend, etc.)
                yield return message;
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<MidiMessage> Tick(double deltaSeconds, double bpm)
    {
        List<MidiMessage> events = [];
        
        lock (_lock)
        {
            // If no notes held, clean up any latching note and reset timing.
            if (_heldNotes.Count == 0)
            {
                if (_lastNotePlayed != -1)
                {
                    events.Add(new MidiMessage((int)MidiCommand.NoteOff, (byte)_lastNotePlayed, 0));
                    _lastNotePlayed = -1;
                }
                _accumulatedTime = 0;
                return events;
            }

            // Calculate duration of a single step based on BPM and Rate.
            double secondsPerBeat = 60.0 / (bpm > 0 ? bpm : 120);
            double stepDuration = secondsPerBeat * Rate;

            _accumulatedTime += deltaSeconds;

            // Check if it's time to trigger the next step.
            if (_accumulatedTime >= stepDuration)
            {
                _accumulatedTime -= stepDuration;

                // 1. Send Note Off for the previous step
                if (_lastNotePlayed != -1)
                {
                    events.Add(new MidiMessage((int)MidiCommand.NoteOff, (byte)_lastNotePlayed, 0));
                }

                // 2. Advance Arpeggiator Logic
                AdvanceIndex();

                // 3. Determine the note to play
                var noteIndex = Mode switch
                {
                    ArpMode.Down => _heldNotes.Count - 1 - _arpIndex,
                    ArpMode.Random => Random.Next(0, _heldNotes.Count),
                    _ => _arpIndex
                };

                // 4. Generate Note On
                if (noteIndex >= 0 && noteIndex < _heldNotes.Count)
                {
                    var baseNote = _heldNotes[noteIndex];
                    var octaveOffset = _arpOctave * 12;
                    var finalNote = Math.Clamp(baseNote + octaveOffset, 0, 127);
                    var vel = Math.Clamp(_lastVelocityPlayed, 1, 127);

                    events.Add(new MidiMessage((int)MidiCommand.NoteOn, (byte)finalNote, (byte)vel));
                    _lastNotePlayed = finalNote;
                }
            }
            // Handle Note Gate (Silence fraction of the step)
            else if (_lastNotePlayed != -1 && _accumulatedTime > (stepDuration * Gate))
            {
                events.Add(new MidiMessage((int)MidiCommand.NoteOff, (byte)_lastNotePlayed, 0));
                _lastNotePlayed = -1;
            }
        }
        
        return events;
    }

    /// <summary>
    /// Resets the arpeggiator's internal state to its starting position.
    /// </summary>
    public void ResetState()
    {
        _arpIndex = -1;
        _arpOctave = 0;
        _arpDirection = 1;
        _lastNotePlayed = -1;
        _accumulatedTime = 0;
    }

    private void AdvanceIndex()
    {
        var octaves = Math.Max(1, Octaves);
        
        _arpIndex += _arpDirection;

        // If we are still within valid notes for the current octave, we are done.
        if (_arpIndex < _heldNotes.Count && _arpIndex >= 0) 
            return;
        
        // Move to next octave
        _arpOctave += _arpDirection;

        // Handle Octave wrapping/bouncing
        if (_arpOctave >= octaves || _arpOctave < 0)
        {
            if (Mode == ArpMode.UpDown)
            {
                // Invert direction and step back
                _arpDirection *= -1;
                _arpOctave += _arpDirection;
                _arpIndex += _arpDirection * 2; // Step back twice from boundary to avoid repeating the peak note
                
                // Boundary correction for single note chords
                if (_arpIndex < 0) _arpIndex = 0;
                if (_arpIndex >= _heldNotes.Count) _arpIndex = Math.Max(0, _heldNotes.Count - 1);
            }
            else
            {
                // Loop back to the start
                _arpOctave = 0;
                _arpIndex = 0;
            }
        }
        else
        {
            // Just wrap the index for the new octave
            _arpIndex = _arpDirection > 0 ? 0 : _heldNotes.Count - 1;
        }
    }
}