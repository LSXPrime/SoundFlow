using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Modifiers;

/// <summary>
/// A Parametric Equalizer with support for multiple filter types.
/// </summary>
public sealed class ParametricEqualizer : SoundModifier
{
    /// <inheritdoc />
    public override string Name { get; set; } = "Parametric Equalizer";

    /// <summary>
    /// List of EQ bands applied by this equalizer.
    /// </summary>
    public List<EqualizerBand> Bands { get; private set; } = [];

    // Dictionary mapping Channel Index -> List of BiquadFilters
    private readonly Dictionary<int, List<BiquadFilter>> _filtersPerChannel = [];
    private readonly AudioFormat _format;

    /// <summary>
    /// Constructs a new instance of <see cref="ParametricEqualizer"/>.
    /// </summary>
    /// <param name="format">The audio format to process.</param>
    public ParametricEqualizer(AudioFormat format)
    {
        _format = format;
    }

    /// <summary>
    /// Initializes the filters for each channel based on the current EQ bands.
    /// </summary>
    private void InitializeFilters()
    {
        _filtersPerChannel.Clear();
        for (var channel = 0; channel < _format.Channels; channel++)
        {
            var filters = new List<BiquadFilter>();
            foreach (var band in Bands)
            {
                var filter = new BiquadFilter();
                filter.Update(band.Type, _format.SampleRate, band.Frequency, band.Q, band.GainDb, band.S);
                filters.Add(filter);
            }

            _filtersPerChannel[channel] = filters;
        }
    }

    /// <inheritdoc/>
    public override void Process(Span<float> buffer, int channels)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            var channel = i % _format.Channels;
            buffer[i] = ProcessSample(buffer[i], channel);
        }
    }

    /// <inheritdoc/>
    public override float ProcessSample(float sample, int channel)
    {
        if (!_filtersPerChannel.TryGetValue(channel, out var channelFilters))
        {
            // Initialize filters for this channel if not already done (lazy init)
            var filters = new List<BiquadFilter>();
            foreach (var band in Bands)
            {
                var filter = new BiquadFilter();
                filter.Update(band.Type, _format.SampleRate, band.Frequency, band.Q, band.GainDb, band.S);
                filters.Add(filter);
            }

            channelFilters = filters;
            _filtersPerChannel[channel] = channelFilters;
        }

        var processedSample = sample;
        foreach (var filter in channelFilters)
        {
            processedSample = filter.Process(processedSample);
        }

        return processedSample;
    }

    /// <summary>
    /// Adds multiple EQ bands to the equalizer and reinitialize the filters.
    /// </summary>
    /// <param name="bands">The EQ bands to add.</param>
    public void AddBands(IEnumerable<EqualizerBand> bands)
    {
        Bands.AddRange(bands);
        InitializeFilters();
    }

    /// <summary>
    /// Adds an EQ band to the equalizer and reinitialize the filters.
    /// </summary>
    /// <param name="band">The EQ band to add.</param>
    public void AddBand(EqualizerBand band)
    {
        Bands.Add(band);
        InitializeFilters();
    }

    /// <summary>
    /// Removes an EQ band from the equalizer and reinitialize the filters.
    /// </summary>
    /// <param name="band">The EQ band to remove.</param>
    public void RemoveBand(EqualizerBand band)
    {
        Bands.Remove(band);
        InitializeFilters();
    }
}

/// <summary>
/// Represents an EQ band with specific parameters.
/// </summary>
/// <param name="type">The type of filter to apply.</param>
/// <param name="frequency">The center frequency of the EQ band in Hz.</param>
/// <param name="gainDb">The gain of the EQ band in decibels.</param>
/// <param name="q">The quality factor of the EQ band.</param>
/// <param name="s">The gain multiplier (shelf slope) of the EQ band.</param>
public class EqualizerBand(FilterType type, float frequency, float gainDb, float q, float s = 1f)
{
    /// <summary>
    /// The center frequency of the EQ band in Hz.
    /// </summary>
    public float Frequency { get; set; } = frequency;

    /// <summary>
    /// The gain of the EQ band in decibels.
    /// </summary>
    public float GainDb { get; set; } = gainDb;

    /// <summary>
    /// The quality factor of the EQ band.
    /// </summary>
    public float Q { get; set; } = q;

    /// <summary>
    /// The gain multiplier of the EQ band.
    /// </summary>
    public float S { get; set; } = s;

    /// <summary>
    /// The type of filter to apply.
    /// </summary>
    public FilterType Type { get; set; } = type;
}