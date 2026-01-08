using System.Buffers;
using SoundFlow.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Modifiers;
using SoundFlow.Structs;
using SoundFlow.Synthesis.Generators;
using SoundFlow.Synthesis.Instruments;
using SoundFlow.Synthesis.Interfaces;

namespace SoundFlow.Synthesis.Voices;

/// <summary>
/// A concrete implementation of IVoice, representing a single synthesizer voice.
/// This version is architected for complex, layered sounds with unison.
/// </summary>
internal sealed class Voice : IVoice
{
    private readonly VoiceContext _context;

    private readonly UnisonLayer[] _unisonLayers;
    private readonly int _unisonCount;

    // Main envelopes that control all unison layers
    private readonly AdsrGenerator _ampEnvelope;
    private readonly AdsrGenerator? _filterEnvelope;
    private readonly Filter? _filter;
    
    // Per-note expression state for MPE
    private float _perNotePitchBend; // In semitones
    private float _perNotePressure;  // Normalized 0-1
    private float _perNoteTimbre;    // Normalized 0-1
    
    /// <inheritdoc />
    public int NoteNumber => _context.NoteNumber;

    /// <inheritdoc />
    public int Velocity => _context.Velocity;

    /// <inheritdoc />
    public bool IsFinished => _ampEnvelope.IsFinished;

    /// <inheritdoc />
    public bool IsReleasing => _ampEnvelope.IsReleasing;

    /// <inheritdoc />
    public bool IsSustained { get; set; }

    /// <summary>
    /// Represents a single oscillator layer within a unison voice.
    /// </summary>
    private class UnisonLayer(IGenerator oscillator, float detuneRatio, float pan)
    {
        public readonly IGenerator Oscillator = oscillator;
        public readonly float DetuneRatio = detuneRatio;
        public readonly float Pan = pan; // 0.0 = Left, 0.5 = Center, 1.0 = Right
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Voice"/> class based on a definition.
    /// </summary>
    /// <param name="definition">The voice definition describing how to build this voice.</param>
    /// <param name="context">The initial context for this voice.</param>
    public Voice(VoiceDefinition definition, VoiceContext context)
    {
        _context = context;

        // Create the main amplitude envelope.
        _ampEnvelope = new AdsrGenerator(definition.Format, definition.AttackTime, definition.DecayTime, definition.SustainLevel, definition.ReleaseTime);

        // Create unison layers
        _unisonCount = definition.Unison;
        _unisonLayers = new UnisonLayer[_unisonCount];
        var panStep = _unisonCount > 1 ? 1.0f / (_unisonCount - 1) : 0.5f; // Center if only one voice
        for (int i = 0; i < _unisonCount; i++)
        {
            var detune = 1.0f + (i - (_unisonCount - 1) / 2.0f) * definition.Detune;
            var pan = _unisonCount > 1 ? i * panStep : 0.5f;
            _unisonLayers[i] = new UnisonLayer(new OscillatorGenerator(definition.OscillatorType), detune, pan);
        }

        // Create filter and filter envelope if required.
        if (definition.UseFilter)
        {
            _filter = new Filter(new AudioFormat { SampleRate = context.SampleRate, Channels = 2 })
            {
                Type = FilterType.LowPass,
                Resonance = 0.5f
            };

            // Make the filter envelope velocity-sensitive
            var filterAttack = 0.05f + (1.0f - (context.Velocity / 127.0f)) * 0.3f;
            _filterEnvelope = new AdsrGenerator(definition.Format, filterAttack, 0.4f, 0.1f, 0.8f);
        }
    }

    /// <inheritdoc />
    public void Render(Span<float> buffer)
    {
        var channels = _context.SampleRate > 0 ? buffer.Length / (int)((buffer.Length / (float)_context.SampleRate) * _context.SampleRate) : 2;
        var frameCount = buffer.Length / channels;

        // 1. Generate master envelopes
        float[]? ampEnvBuffer = null;
        float[]? filterEnvBuffer = null;
        try
        {
            ampEnvBuffer = ArrayPool<float>.Shared.Rent(frameCount);
            var ampEnvSpan = ampEnvBuffer.AsSpan(0, frameCount);
            _ampEnvelope.Generate(ampEnvSpan, _context);

            if (_filterEnvelope != null)
            {
                filterEnvBuffer = ArrayPool<float>.Shared.Rent(frameCount);
                var filterEnvSpan = filterEnvBuffer.AsSpan(0, frameCount);
                _filterEnvelope.Generate(filterEnvSpan, _context);
            }

            // 2. Render and mix unison layers
            float[]? layerBuffer = null;
            try
            {
                // Allocate a buffer for a single mono frame's worth of data.
                layerBuffer = ArrayPool<float>.Shared.Rent(frameCount);
                var layerSpan = layerBuffer.AsSpan(0, frameCount);

                buffer.Clear(); // Start with a clean buffer to mix into
                for (var i = 0; i < _unisonCount; i++)
                {
                    var layer = _unisonLayers[i];

                    // Apply unison detune and MPE per-note pitch bend
                    var pitchBendSemitones = _perNotePitchBend + _context.ChannelPitchBend;
                    var frequencyMultiplier = MathF.Pow(2.0f, pitchBendSemitones / 12.0f);
                    _context.Frequency = _context.BaseFrequency * layer.DetuneRatio * frequencyMultiplier;
                    
                    // Generate a mono signal for the current layer
                    layer.Oscillator.Generate(layerSpan, _context);

                    // Apply panning and mix into the main multichannel buffer
                    if (channels == 2) // Optimized path for stereo
                    {
                        var panAngle = layer.Pan * MathF.PI / 2.0f;
                        var leftGain = MathF.Cos(panAngle);
                        var rightGain = MathF.Sin(panAngle);
                        
                        for (var j = 0; j < frameCount; j++)
                        {
                            var monoSample = layerSpan[j];
                            buffer[j * 2] += monoSample * leftGain;
                            buffer[j * 2 + 1] += monoSample * rightGain;
                        }
                    }
                    else // Generic path for any channel count
                    {
                        for (var j = 0; j < frameCount; j++)
                        {
                            for (var c = 0; c < channels; c++)
                            {
                                // Simple panning for multichannel: fade between first two channels
                                var gain = c == 0 ? 1.0f - layer.Pan : (c == 1 ? layer.Pan : 0.5f);
                                buffer[j * channels + c] += layerSpan[j] * gain;
                            }
                        }
                    }
                }
            }
            finally
            {
                if(layerBuffer != null) ArrayPool<float>.Shared.Return(layerBuffer);
            }

            // Normalize the mixed unison signal to prevent clipping before filtering
            if (_unisonCount > 1)
            {
                var normFactor = 1.0f / MathF.Sqrt(_unisonCount);
                for (var i = 0; i < buffer.Length; i++)
                {
                    buffer[i] *= normFactor;
                }
            }

            // 3. Apply the main filter to the mixed signal
            if (_filter != null && filterEnvBuffer != null)
            {
                var filterEnvSpan = filterEnvBuffer.AsSpan(0, frameCount);
                for (var i = 0; i < frameCount; i++)
                {
                    // Modulate cutoff with envelope, velocity, pressure, and timbre
                    var velocityInfluence = (_context.Velocity / 127.0f) * 4000f;
                    var pressureInfluence = _perNotePressure * 2000f;
                    var timbreInfluence = _perNoteTimbre * 3000f;
                    _filter.CutoffFrequency = 200f + velocityInfluence + pressureInfluence + timbreInfluence + (filterEnvSpan[i] * 8000f);
                    
                    for (var c = 0; c < channels; c++)
                    {
                        var sampleIndex = i * channels + c;
                        buffer[sampleIndex] = _filter.ProcessSample(buffer[sampleIndex], c);
                    }
                }
            }
            
            // 4. Apply the final amplitude envelope
            for (var i = 0; i < frameCount; i++)
            {
                var envelopeValue = ampEnvSpan[i];
                for (var c = 0; c < channels; c++)
                {
                    buffer[i * channels + c] *= envelopeValue;
                }
            }
        }
        finally
        {
            if (ampEnvBuffer != null) ArrayPool<float>.Shared.Return(ampEnvBuffer);
            if (filterEnvBuffer != null) ArrayPool<float>.Shared.Return(filterEnvBuffer);
        }
    }

    /// <inheritdoc />
    public void NoteOff()
    {
        _ampEnvelope.NoteOff();
        _filterEnvelope?.NoteOff();
    }

    /// <inheritdoc />
    public void Kill()
    {
        _ampEnvelope.Kill();
        _filterEnvelope?.Kill();
    }

    /// <inheritdoc />
    public void ProcessMidiControl(MidiMessage message, float channelPitchBend)
    {
        _context.ChannelPitchBend = channelPitchBend;
    }

    /// <inheritdoc />
    public void SetPerNotePitchBend(float semitones)
    {
        _perNotePitchBend = semitones;
    }

    /// <inheritdoc />
    public void SetPerNotePressure(float value)
    {
        _perNotePressure = value;
    }

    /// <inheritdoc />
    public void SetPerNoteTimbre(float value)
    {
        _perNoteTimbre = value;
    }
}