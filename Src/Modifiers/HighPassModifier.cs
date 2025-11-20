using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Modifiers;

/// <summary>
/// A sound modifier that implements a high-pass filter.
/// </summary>
public class HighPassModifier : SoundModifier
{
    private readonly float[] _previousOutput;
    private readonly float[] _previousSample;
    private float _cutoffFrequency;
    private readonly AudioFormat _format;

    /// <summary>
    /// Initializes a new instance of the <see cref="HighPassModifier"/> class.
    /// </summary>
    /// <param name="format">The audio format to process.</param>
    /// <param name="cutoffFrequency">The cutoff frequency of the filter.</param>
    public HighPassModifier(AudioFormat format, float cutoffFrequency)
    {
        _format = format;
        _previousOutput = new float[format.Channels];
        _previousSample = new float[format.Channels];
        CutoffFrequency = cutoffFrequency;
    }

    /// <summary>
    /// Gets or sets the cutoff frequency of the filter.
    /// </summary>
    [ControllableParameter("Cutoff", 20.0, 20000.0, MappingScale.Logarithmic)]
    public float CutoffFrequency
    {
        get => _cutoffFrequency;
        set => _cutoffFrequency = Math.Max(20, value); // Minimum 20Hz
    }
    
    /// <inheritdoc />
    public override void ProcessMidiMessage(MidiMessage message)
    {
        if (message is { Command: MidiCommand.ControlChange, ControllerNumber: 74 })
        {
            var normalizedCutoff = message.ControllerValue / 127.0f;
            var minLog = MathF.Log(20.0f);
            var maxLog = MathF.Log(20000.0f);
            CutoffFrequency = MathF.Exp(minLog + (maxLog - minLog) * normalizedCutoff);
        }
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel)
    {
        var dt = _format.InverseSampleRate;
        var rc = 1f / (2 * MathF.PI * _cutoffFrequency);
        var alpha = rc / (rc + dt);
        var output = alpha * (_previousOutput[channel] + sample - _previousSample[channel]);
        _previousOutput[channel] = output;
        _previousSample[channel] = sample;
        return output;
    }
}