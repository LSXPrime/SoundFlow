using System.Buffers;
using SoundFlow.Midi.Structs;
using SoundFlow.Synthesis.Generators;
using SoundFlow.Synthesis.Instruments;
using SoundFlow.Synthesis.Interfaces;

namespace SoundFlow.Synthesis.Voices;

/// <summary>
/// A specialized IVoice implementation for playing back samples from a SoundFont.
/// </summary>
internal sealed class SampleVoice(VoiceDefinition definition, VoiceContext context) : IVoice
{
    private readonly AdsrGenerator _ampEnvelope = new(definition.Format, definition.AttackTime, definition.DecayTime, definition.SustainLevel, definition.ReleaseTime);
    private readonly SamplerGenerator _sampler = new(
        definition.Sample!.Data,
        definition.Sample.RootKey,
        isLooping: definition.Sample.StartLoop < definition.Sample.EndLoop
    );

    // Per-note expression state for MPE
    private float _perNotePitchBend;

    public int NoteNumber => context.NoteNumber;
    public int Velocity => context.Velocity;
    public bool IsFinished => _ampEnvelope.IsFinished;
    public bool IsReleasing => _ampEnvelope.IsReleasing;
    public bool IsSustained { get; set; }

    public void Render(Span<float> buffer)
    {
        float[]? ampEnvBuffer = null;
        try
        {
            ampEnvBuffer = ArrayPool<float>.Shared.Rent(buffer.Length);
            var ampEnvSpan = ampEnvBuffer.AsSpan(0, buffer.Length);

            // Apply pitch bend before generating samples
            var totalPitchBend = _perNotePitchBend + context.ChannelPitchBend;
            var frequencyMultiplier = MathF.Pow(2.0f, totalPitchBend / 12.0f);
            var renderContext = context with { Frequency = context.BaseFrequency * frequencyMultiplier };

            _sampler.Generate(buffer, renderContext);
            _ampEnvelope.Generate(ampEnvSpan, renderContext);

            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] *= ampEnvSpan[i];
            }
        }
        finally
        {
            if (ampEnvBuffer != null) ArrayPool<float>.Shared.Return(ampEnvBuffer);
        }
    }

    public void NoteOff()
    {
        _ampEnvelope.NoteOff();
    }

    public void Kill()
    {
        _ampEnvelope.Kill();
    }

    public void ProcessMidiControl(MidiMessage message, float channelPitchBend)
    {
        context.ChannelPitchBend = channelPitchBend;
    }

    public void SetPerNotePitchBend(float semitones)
    {
        _perNotePitchBend = semitones;
    }

    public void SetPerNotePressure(float value)
    {
        // Sample-based voices do not typically respond to pressure by default.
    }

    public void SetPerNoteTimbre(float value)
    {
        // Sample-based voices do not typically respond to timbre by default.
    }
}