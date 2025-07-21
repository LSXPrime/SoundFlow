
using SoundFlow.Modifiers;

namespace SoundFlow.Samples.SimplePlayer;

public static class EqualizerPresets
{
    public static Dictionary<string, List<EqualizerBand>> GetAllPresets()
    {
        return new()
        {
            ["Default"] = DefaultEq(),
            ["Metal"] = MetalEq(),
            ["HipHop"] = HipHopEq(),
            ["Acoustic"] = AcousticEq(),
            ["Jazz"] = JazzEq(),
            ["Classical"] = ClassicalEq(),
            ["Vocal Boost"] = VocalBoostEq(),
            ["Bass Boost"] = BassBoostEq(),
            ["Pop"] = PopEq(),
            ["Rock"] = RockEq(),
            ["Party"] = PartyEq(),
            ["Rap"] = RapEq(),
            ["Live"] = LiveEq(),
            ["Large Hall"] = LargeHallEq(),
            ["JPop"] = JPopEq(),
            ["EDM"] = EdmEq(),
            ["Podcast"] = PodcastEq(),
            ["Nightcore"] = NightcoreEq(),
        };
    }

    public static List<EqualizerBand> DefaultEq() =>
    [
        new(FilterType.Peaking, 1000, 6, 1.4f),
        new(FilterType.LowShelf, 250, -10, 0.7f),
        new(FilterType.HighShelf, 5000, 3, 0.7f),
        new(FilterType.BandPass, 2000, 0, 3.0f),
        new(FilterType.Notch, 500, 0, 2.0f),
        new(FilterType.LowPass, 150, 0, 0.7f),
        new(FilterType.HighPass, 10000, 0, 0.7f),
    ];

    public static List<EqualizerBand> MetalEq() =>
    [
        new(FilterType.Peaking, 80, 9, 2.0f),
        new(FilterType.Peaking, 250, 3, 1.4f),
        new(FilterType.Peaking, 500, -3, 1.4f),
        new(FilterType.Peaking, 1000, -2, 1.4f),
        new(FilterType.Peaking, 4000, 4, 1.4f),
        new(FilterType.Peaking, 8000, 6, 1.4f),
        new(FilterType.HighShelf, 12000, 4, 0.7f),
    ];

    public static List<EqualizerBand> HipHopEq() =>
    [
        new(FilterType.LowShelf, 80, 6, 0.7f),
        new(FilterType.Peaking, 100, 4, 1.4f),
        new(FilterType.Peaking, 300, -2, 1.4f),
        new(FilterType.Peaking, 1000, 1, 1.4f),
        new(FilterType.Peaking, 3000, 2, 1.4f),
        new(FilterType.HighShelf, 8000, 3, 0.7f),
    ];

    public static List<EqualizerBand> AcousticEq() =>
    [
        new(FilterType.HighPass, 80, 0, 0.7f),
        new(FilterType.Peaking, 200, -2, 1.4f),
        new(FilterType.Peaking, 3000, 2, 1.4f),
        new(FilterType.Peaking, 5000, 3, 1.4f),
        new(FilterType.HighShelf, 10000, 2, 0.7f),
    ];

    public static List<EqualizerBand> JazzEq() =>
    [
        new(FilterType.Peaking, 60, 2, 1.4f),
        new(FilterType.Peaking, 400, -2, 1.4f),
        new(FilterType.Peaking, 1000, 1, 1.4f),
        new(FilterType.Peaking, 5000, 2, 1.4f),
        new(FilterType.HighShelf, 12000, 1, 0.7f),
    ];

    public static List<EqualizerBand> ClassicalEq() =>
    [
        new(FilterType.HighPass, 40, 0, 0.7f),
        new(FilterType.Peaking, 250, -1, 1.4f),
        new(FilterType.Peaking, 4000, 1, 2.0f),
        new(FilterType.HighShelf, 10000, 1, 0.7f),
    ];

    public static List<EqualizerBand> VocalBoostEq() =>
    [
        new(FilterType.HighPass, 100, 0, 0.7f),
        new(FilterType.Peaking, 250, -2, 1.4f),
        new(FilterType.Peaking, 3000, 3, 1.4f),
        new(FilterType.Peaking, 5000, 2, 1.4f),
        new(FilterType.HighShelf, 10000, 1, 0.7f),
    ];

    public static List<EqualizerBand> BassBoostEq() =>
    [
        new(FilterType.LowShelf, 100, 6, 0.7f),
        new(FilterType.Peaking, 300, -3, 1.4f),
    ];

    public static List<EqualizerBand> PopEq() =>
    [
        new(FilterType.Peaking, 100, 3, 1.4f),
        new(FilterType.Peaking, 300, -2, 1.4f),
        new(FilterType.Peaking, 1000, 1, 1.4f),
        new(FilterType.Peaking, 3000, 2, 1.4f),
        new(FilterType.Peaking, 8000, 2, 1.4f),
        new(FilterType.HighShelf, 12000, 1, 0.7f),
    ];

    public static List<EqualizerBand> RockEq() =>
    [
        new(FilterType.Peaking, 80, 4, 2.0f),
        new(FilterType.Peaking, 500, -2, 1.4f),
        new(FilterType.Peaking, 1000, -1, 1.4f),
        new(FilterType.Peaking, 4000, 3, 1.4f),
        new(FilterType.Peaking, 8000, 2, 1.4f),
    ];

    public static List<EqualizerBand> PartyEq() =>
    [
        new(FilterType.LowShelf, 100, 5, 0.7f),
        new(FilterType.Peaking, 300, -2, 1.4f),
        new(FilterType.Peaking, 4000, 2, 1.4f),
        new(FilterType.HighShelf, 10000, 4, 0.7f),
    ];

    public static List<EqualizerBand> RapEq() =>
    [
        new(FilterType.LowShelf, 80, 7, 0.7f),
        new(FilterType.Peaking, 100, 3, 1.4f),
        new(FilterType.Peaking, 300, -3, 1.4f),
        new(FilterType.Peaking, 3000, 3, 1.4f),
        new(FilterType.HighShelf, 8000, 2, 0.7f),
    ];

    public static List<EqualizerBand> LiveEq() =>
    [
        new(FilterType.HighPass, 60, 0, 0.7f),
        new(FilterType.Peaking, 250, -3, 1.4f),
        new(FilterType.Peaking, 4000, 2, 1.4f),
        new(FilterType.Peaking, 8000, -2, 1.4f),
    ];

    public static List<EqualizerBand> LargeHallEq() =>
    [
        new(FilterType.HighPass, 50, 0, 0.7f),
        new(FilterType.Peaking, 200, -4, 1.4f),
        new(FilterType.Peaking, 4000, 2, 1.4f),
        new(FilterType.Peaking, 8000, -1, 1.4f),
    ];

    public static List<EqualizerBand> JPopEq() =>
    [
        new(FilterType.Peaking, 100, 2, 1.4f),
        new(FilterType.Peaking, 300, -2, 1.4f),
        new(FilterType.Peaking, 1000, 1, 1.4f),
        new(FilterType.Peaking, 3000, 3, 1.4f),
        new(FilterType.Peaking, 6000, 2, 2.0f),
        new(FilterType.HighShelf, 12000, 2, 0.7f),
    ];

    public static List<EqualizerBand> EdmEq() =>
    [
        new(FilterType.LowShelf, 60, 4, 0.7f),
        new(FilterType.Peaking, 100, 3, 1.4f),
        new(FilterType.Peaking, 300, -2, 1.4f),
        new(FilterType.Peaking, 5000, 2, 2.0f),
        new(FilterType.HighShelf, 10000, 3, 0.7f),
    ];

    public static List<EqualizerBand> PodcastEq() =>
    [
        new(FilterType.HighPass, 100, 0, 0.7f),
        new(FilterType.Peaking, 250, -3, 1.4f),
        new(FilterType.Peaking, 4000, 2, 1.4f),
        new(FilterType.Peaking, 8000, -2, 1.4f),
    ];

    public static List<EqualizerBand> NightcoreEq() =>
    [
        new(FilterType.Peaking, 100, 2, 1.4f),
        new(FilterType.Peaking, 300, -2, 1.4f),
        new(FilterType.Peaking, 3000, 3, 1.4f),
        new(FilterType.Peaking, 6000, 2, 2.0f),
        new(FilterType.HighShelf, 10000, 2, 0.7f),
    ];
}