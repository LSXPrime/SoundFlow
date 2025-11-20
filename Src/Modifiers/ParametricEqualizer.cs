using SoundFlow.Abstracts;
using SoundFlow.Structs;

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

    private readonly Dictionary<int, List<BiquadFilter>> _filtersPerChannel = [];
    private readonly AudioFormat _format;

    /// <summary>
    /// Constructs a new instance of <see cref="ParametricEqualizer"/>.
    /// </summary>
    /// <param name="format">The audio format to process.</param>
    public ParametricEqualizer(AudioFormat format)
    {
        _format = format; // Store the format
    }

    /// <summary>
    /// Initializes the filters for each channel based on the current EQ bands.
    /// </summary>
    private void InitializeFilters()
    {
        _filtersPerChannel.Clear();
        for (var channel = 0; channel < _format.Channels; channel++)
        {
            List<BiquadFilter> filters = [];
            foreach (var band in Bands)
            {
                var filter = new BiquadFilter();
                filter.UpdateCoefficients(band, _format.SampleRate);
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
        if (!_filtersPerChannel.TryGetValue(channel, out var value))
        {
            // Initialize filters for this channel if not already done
            var filters = new List<BiquadFilter>();
            foreach (var band in Bands)
            {
                var filter = new BiquadFilter();
                filter.UpdateCoefficients(band, _format.SampleRate);
                filters.Add(filter);
            }

            value = filters;
            _filtersPerChannel[channel] = value;
        }

        var processedSample = sample;
        foreach (var filter in value)
        {
            processedSample = filter.ProcessSample(processedSample);
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
/// Types of filters supported by the Parametric Equalizer.
/// </summary>
public enum FilterType
{
    /// <summary>
    /// A peaking equalizer boosts or cuts a specific frequency range.
    /// </summary>
    Peaking,

    /// <summary>
    /// A low-shelf equalizer boosts or cuts all frequencies below a specific frequency.
    /// </summary>
    LowShelf,

    /// <summary>
    /// A high-shelf equalizer boosts or cuts all frequencies above a specific frequency.
    /// </summary>
    HighShelf,

    /// <summary>
    /// A low-pass filter removes high frequencies from the audio signal.
    /// </summary>
    LowPass,

    /// <summary>
    /// A high-pass filter removes low frequencies from the audio signal.
    /// </summary>
    HighPass,

    /// <summary>
    /// A band-pass filter removes all frequencies outside a specific frequency range.
    /// </summary>
    BandPass,

    /// <summary>
    /// A notch filter removes a specific frequency range from the audio signal.
    /// </summary>
    Notch,

    /// <summary>
    /// An all-pass filter changes the phase of the audio signal without affecting its frequency response.
    /// </summary>
    AllPass
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

/// <summary>
/// A biquad filter used to process audio samples.
/// </summary>
public class BiquadFilter
{
    private float _a0, _a1, _a2, _b0, _b1, _b2;
    private float _x1, _x2, _y1, _y2;

    /// <summary>
    /// Updates the filter coefficients based on the specified EQ band parameters.
    /// </summary>
    /// <param name="band">The EQ band containing filter parameters.</param>
    /// <param name="sampleRate">The sample rate of the audio data.</param>
    public void UpdateCoefficients(EqualizerBand band, float sampleRate)
    {
        float a;
        var omega = 2 * (float)Math.PI * band.Frequency / sampleRate;
        var sinOmega = (float)Math.Sin(omega);
        var cosOmega = (float)Math.Cos(omega);
        float alpha;

        switch (band.Type)
        {
            case FilterType.Peaking:
                a = (float)Math.Pow(10, band.GainDb / 40);
                alpha = sinOmega / (2 * band.Q);

                _b0 = 1 + alpha * a;
                _b1 = -2 * cosOmega;
                _b2 = 1 - alpha * a;
                _a0 = 1 + alpha / a;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha / a;
                break;
            case FilterType.LowShelf:
                a = (float)Math.Pow(10, band.GainDb / 40);
                var sqrtA = (float)Math.Sqrt(a);
                alpha = sinOmega / 2 * (float)Math.Sqrt((a + 1 / a) * (1 / band.S - 1) + 2);

                _b0 = a * ((a + 1) - (a - 1) * cosOmega + 2 * sqrtA * alpha);
                _b1 = 2 * a * ((a - 1) - (a + 1) * cosOmega);
                _b2 = a * ((a + 1) - (a - 1) * cosOmega - 2 * sqrtA * alpha);
                _a0 = (a + 1) + (a - 1) * cosOmega + 2 * sqrtA * alpha;
                _a1 = -2 * ((a - 1) + (a + 1) * cosOmega);
                _a2 = (a + 1) + (a - 1) * cosOmega - 2 * sqrtA * alpha;
                break;
            case FilterType.HighShelf:
                a = (float)Math.Pow(10, band.GainDb / 40);
                sqrtA = (float)Math.Sqrt(a);
                alpha = sinOmega / 2 * (float)Math.Sqrt((a + 1 / a) * (1 / band.S - 1) + 2);

                _b0 = a * ((a + 1) + (a - 1) * cosOmega + 2 * sqrtA * alpha);
                _b1 = -2 * a * ((a - 1) + (a + 1) * cosOmega);
                _b2 = a * ((a + 1) + (a - 1) * cosOmega - 2 * sqrtA * alpha);
                _a0 = (a + 1) - (a - 1) * cosOmega + 2 * sqrtA * alpha;
                _a1 = 2 * ((a - 1) - (a + 1) * cosOmega);
                _a2 = (a + 1) - (a - 1) * cosOmega - 2 * sqrtA * alpha;
                break;
            case FilterType.LowPass:
                alpha = sinOmega / (2 * band.Q);

                _b0 = (1 - cosOmega) / 2;
                _b1 = 1 - cosOmega;
                _b2 = (1 - cosOmega) / 2;
                _a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;
            case FilterType.HighPass:
                alpha = sinOmega / (2 * band.Q);

                _b0 = (1 + cosOmega) / 2;
                _b1 = -(1 + cosOmega);
                _b2 = (1 + cosOmega) / 2;
                _a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;
            case FilterType.BandPass:
                alpha = sinOmega / (2 * band.Q);

                _b0 = alpha;
                _b1 = 0;
                _b2 = -alpha;
                _a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;
            case FilterType.Notch:
                alpha = sinOmega / (2 * band.Q);

                _b0 = 1;
                _b1 = -2 * cosOmega;
                _b2 = 1;
                _a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;
            case FilterType.AllPass:
                alpha = sinOmega / (2 * band.Q);

                _b0 = 1 - alpha;
                _b1 = -2 * cosOmega;
                _b2 = 1 + alpha;
                _a0 = 1 + alpha;
                _a1 = -2 * cosOmega;
                _a2 = 1 - alpha;
                break;
            default:
                throw new NotSupportedException("Filter type not supported or implemented");
        }

        // Normalize the coefficients
        _b0 /= _a0;
        _b1 /= _a0;
        _b2 /= _a0;
        _a1 /= _a0;
        _a2 /= _a0;
    }

    /// <summary>
    /// Processes a single audio sample through the biquad filter.
    /// </summary>
    /// <param name="x">The input sample.</param>
    /// <returns>The filtered output sample.</returns>
    public float ProcessSample(float x)
    {
        var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;

        // Shift the data
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;

        return y;
    }
}