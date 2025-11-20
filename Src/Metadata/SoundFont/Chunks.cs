namespace SoundFlow.Metadata.SoundFont;

/// <summary>
/// A container for parsed preset records from an SF2 file.
/// </summary>
internal sealed class PresetChunk(PresetRecord[] presets)
{
    public readonly PresetRecord[] Presets = presets;
}

/// <summary>
/// A container for parsed instrument records from an SF2 file.
/// </summary>
internal sealed class InstrumentChunk(InstrumentRecord[] instruments)
{
    public readonly InstrumentRecord[] Instruments = instruments;
}

/// <summary>
/// A container for parsed bag (zone) records from an SF2 file.
/// </summary>
internal sealed class BagChunk(BagRecord[] bags)
{
    public readonly BagRecord[] Bags = bags;
}

/// <summary>
/// A container for parsed generator records from an SF2 file.
/// </summary>
internal sealed class GeneratorChunk(GeneratorRecord[] generators)
{
    public readonly GeneratorRecord[] Generators = generators;
}

/// <summary>
/// A container for parsed sample header records from an SF2 file.
/// </summary>
internal sealed class SampleHeaderChunk(SampleHeaderRecord[] sampleHeaders)
{
    public readonly SampleHeaderRecord[] SampleHeaders = sampleHeaders;
}

/// <summary>
/// A high-level container for all parsed metadata chunks from an SF2 file.
/// </summary>
internal sealed class ParsedSoundFont
{
    public PresetChunk? Presets { get; set; }
    public BagChunk? PresetBags { get; set; }
    public GeneratorChunk? PresetGenerators { get; set; }
    public InstrumentChunk? Instruments { get; set; }
    public BagChunk? InstrumentBags { get; set; }
    public GeneratorChunk? InstrumentGenerators { get; set; }
    public SampleHeaderChunk? SampleHeaders { get; set; }
}