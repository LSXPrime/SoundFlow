using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Midi.Enums;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Modifiers;

/// <summary>
/// Implements a digital biquad filter, allowing for various filter types such as LowPass, HighPass, BandPass, and Notch.
/// </summary>
public class Filter : SoundModifier
{
    // One filter instance per channel to maintain independent state
    private readonly BiquadFilter[] _filters;
    private readonly AudioFormat _format;
    
    // Parameters
    private FilterType _type = FilterType.LowPass;
    private float _cutoffFrequency = 1000f;
    private float _resonance = 0.7f;

    /// <summary>
    /// Initializes a new instance of the <see cref="Filter"/> class with default settings.
    /// </summary>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    public Filter(AudioFormat format)
    {
        _format = format;
        _filters = new BiquadFilter[format.Channels];
        for (var i = 0; i < format.Channels; i++)
        {
            _filters[i] = new BiquadFilter();
        }
        UpdateCoefficients();
    }

    /// <inheritdoc/>
    public override string Name { get; set; } = "Filter";

    /// <summary>
    /// Gets or sets the type of filter.
    /// Changing the filter type recalculates the filter coefficients.
    /// </summary>
    [ControllableParameter("Filter Type", 0, 3)]
    public FilterType Type
    {
        get => _type;
        set
        {
            _type = value;
            UpdateCoefficients();
        }
    }

    /// <summary>
    /// Gets or sets the cutoff frequency of the filter in Hertz.
    /// This frequency determines the point at which the filter starts to attenuate the signal.
    /// Changing the cutoff frequency recalculates the filter coefficients.
    /// </summary>
    [ControllableParameter("Cutoff", 20.0, 20000.0, MappingScale.Logarithmic)]
    public float CutoffFrequency
    {
        get => _cutoffFrequency;
        set
        {
            _cutoffFrequency = value;
            UpdateCoefficients();
        }
    }

    /// <summary>
    /// Gets or sets the resonance of the filter, a value between 0 and 1.
    /// Higher resonance values emphasize frequencies around the cutoff frequency.
    /// </summary>
    [ControllableParameter("Resonance", 0.0, 1.0)]
    public float Resonance
    {
        get => _resonance;
        set
        {
            _resonance = value;
            UpdateCoefficients();
        }
    }

    /// <inheritdoc/>
    public override void ProcessMidiMessage(MidiMessage message)
    {
        if (message.Command != MidiCommand.ControlChange) return;

        switch (message.ControllerNumber)
        {
            // Standard CC for Filter Cutoff (Brightness)
            case 74:
                // Map MIDI value (0-127) to logarithmic frequency range (20Hz - 20kHz)
                var normalizedCutoff = message.ControllerValue / 127.0f;
                var minLog = MathF.Log(20.0f);
                var maxLog = MathF.Log(20000.0f);
                CutoffFrequency = MathF.Exp(minLog + (maxLog - minLog) * normalizedCutoff);
                break;
            
            // Standard CC for Filter Resonance (Timbre/Harmonic Content)
            case 71:
                // Map MIDI value (0-127) to resonance range (0.0 - 1.0)
                Resonance = message.ControllerValue / 127.0f;
                break;
        }
    }

    /// <inheritdoc/>
    public override float ProcessSample(float sample, int channel)
    {
        // Use the filter instance dedicated to this channel
        if (channel < _filters.Length)
        {
            return _filters[channel].Process(sample);
        }
        return sample;
    }

    /// <summary>
    /// Calculates and updates the biquad filter coefficients for all channels.
    /// </summary>
    private void UpdateCoefficients()
    {
        foreach (var filter in _filters)
        {
            filter.Update(_type, _format.SampleRate, _cutoffFrequency, _resonance);
        }
    }
}