using System.Text;
using SoundFlow.Components;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.SoundFont;
using SoundFlow.Structs;
using SoundFlow.Synthesis.Instruments;
using SoundFlow.Synthesis.Interfaces;

namespace SoundFlow.Synthesis.Banks;

/// <summary>
/// A simple data structure to hold information about a preset in a SoundFont.
/// </summary>
public record PresetInfo(int Bank, int Program, string Name);

/// <summary>
/// An IInstrumentBank implementation that loads instruments from a SoundFont 2 (SF2) file.
/// </summary>
public sealed class SoundFontBank : IInstrumentBank, IDisposable
{
    private readonly Dictionary<(int bank, int program), Instrument> _instruments = new();
    private readonly Instrument _fallbackInstrument;
    private readonly FileStream _stream;

    /// <summary>
    /// Gets a read-only list of all presets (programs) available in this SoundFont bank, sorted by bank and program number.
    /// </summary>
    public IReadOnlyList<PresetInfo> AvailablePresets { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SoundFontBank"/> class by parsing an SF2 file.
    /// </summary>
    /// <param name="filePath">The path to the SF2 file.</param>
    /// <param name="format">The audio format of the synthesizer engine.</param>
    public SoundFontBank(string filePath, AudioFormat format)
    {
        _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var parsedData = SoundFontParser.Parse(_stream);
        if (parsedData.SampleHeaders == null)
            throw new InvalidDataException("SoundFont file is missing sample header ('shdr') chunk.");

        // Populate the list of available presets
        var presets = new List<PresetInfo>();
        if (parsedData.Presets != null)
        {
            // The last preset is a mandatory terminator record and should be ignored.
            for (var i = 0; i < parsedData.Presets.Presets.Length - 1; i++)
            {
                var presetRecord = parsedData.Presets.Presets[i];
                var name = GetString(presetRecord.Name);
                presets.Add(new PresetInfo(presetRecord.Bank, presetRecord.Preset, name));
            }
        }

        // Sort the list for predictable, user-friendly ordering.
        presets.Sort((a, b) =>
        {
            var bankCompare = a.Bank.CompareTo(b.Bank);
            return bankCompare != 0 ? bankCompare : a.Program.CompareTo(b.Program);
        });
        AvailablePresets = presets.AsReadOnly();

        var sampleProvider = new SoundFontSampleProvider(_stream, parsedData.SampleHeaders, format.SampleRate);

        BuildInstruments(parsedData, sampleProvider, format);

        // Create the internal fallback instrument for this specific bank
        var fallbackDef = new VoiceDefinition(format, Oscillator.WaveformType.Sine, 1, 0, 0.01f, 0.1f, 0.0f, 0.2f);
        _fallbackInstrument = new Instrument([], fallbackDef, isFallback: true);
    }

    /// <inheritdoc />
    public Instrument GetInstrument(int bank, int program)
    {
        var lookupBank = bank >= 128 ? 128 : bank;
        return _instruments.GetValueOrDefault((lookupBank, program), _fallbackInstrument);
    }

    private void BuildInstruments(ParsedSoundFont sf, SoundFontSampleProvider sampleProvider, AudioFormat format)
    {
        if (sf.Presets == null || sf.PresetBags == null || sf.PresetGenerators == null ||
            sf.Instruments == null || sf.InstrumentBags == null || sf.InstrumentGenerators == null ||
            sf.SampleHeaders == null)
            return; // Not a valid soundfont

        var instrumentRecords = sf.Instruments.Instruments;
        
        // The last record in each list is a mandatory terminator, so we iterate up to Length - 1.
        for (var presetIndex = 0; presetIndex < sf.Presets.Presets.Length - 1; presetIndex++)
        {
            var presetRecord = sf.Presets.Presets[presetIndex];
            var mappings = new List<VoiceMapping>();

            // Determine the range of bags (zones) for this preset.
            var presetBagStartIndex = presetRecord.PresetBagIndex;
            var presetBagEndIndex = sf.Presets.Presets[presetIndex + 1].PresetBagIndex;

            // The first bag in a preset's range is the "global" zone for that preset. We skip it in the main loop.
            var globalPresetZoneGenerators = GetZoneGenerators(presetBagStartIndex, sf.PresetBags, sf.PresetGenerators);

            // Iterate through the preset's local zones.
            for (var pbagIndex = presetBagStartIndex + 1; pbagIndex < presetBagEndIndex; pbagIndex++)
            {
                var presetZoneGenerators = GetZoneGenerators(pbagIndex, sf.PresetBags, sf.PresetGenerators);
                // Combine local preset zone generators with global preset zone generators.
                var combinedPresetGenerators = presetZoneGenerators.Concat(globalPresetZoneGenerators).ToList();

                var instrumentId = GetGeneratorValue(combinedPresetGenerators, GeneratorType.Instrument);
                if (instrumentId == null || instrumentId.Value >= instrumentRecords.Length - 1) continue;

                var instrumentRecord = instrumentRecords[instrumentId.Value];

                // Determine the range of bags (zones) for this instrument.
                var instBagStartIndex = instrumentRecord.InstrumentBagIndex;
                var instBagEndIndex = instrumentRecords[instrumentId.Value + 1].InstrumentBagIndex;

                // The first bag is the "global" instrument zone.
                var globalInstZoneGenerators =
                    GetZoneGenerators(instBagStartIndex, sf.InstrumentBags, sf.InstrumentGenerators);

                // Iterate through the instrument's local zones.
                for (var ibagIndex = instBagStartIndex + 1; ibagIndex < instBagEndIndex; ibagIndex++)
                {
                    var instZoneGenerators = GetZoneGenerators(ibagIndex, sf.InstrumentBags, sf.InstrumentGenerators);
                    // Combine local instrument zone with global instrument zone.
                    var combinedInstGenerators = instZoneGenerators.Concat(globalInstZoneGenerators).ToList();

                    var sampleId = GetGeneratorValue(combinedInstGenerators, GeneratorType.SampleID);
                    if (sampleId == null || sampleId.Value >= sampleProvider.Samples.Count) continue;

                    // Combine all generators: instrument zone -> preset zone
                    var allGenerators = combinedInstGenerators.Concat(combinedPresetGenerators).ToList();

                    var (voiceDef, mapping) =
                        CreateMappingFromGenerators(allGenerators, sampleProvider.Samples[sampleId.Value], format);
                    if (voiceDef != null && mapping != null)
                    {
                        mappings.Add(mapping);
                    }
                }
            }

            if (mappings.Count > 0)
            {
                var fallbackDef =
                    new VoiceDefinition(format, Oscillator.WaveformType.Sine, 1, 0, 0.01f, 0.2f, 0.7f, 0.3f);
                _instruments[(presetRecord.Bank, presetRecord.Preset)] = new Instrument(mappings, fallbackDef);
            }
        }
    }

    private static (VoiceDefinition?, VoiceMapping?) CreateMappingFromGenerators(List<GeneratorRecord> generators,
            SampleData sample, AudioFormat format)
        {
            // SF2 sustain is in centibels (cb) of attenuation. 0cb = 0dB attenuation (full volume). 1000cb = 100dB attenuation (silence).
            var sustainCb = GenValue(GeneratorType.SustainVolEnv);
            var sustainLinear = MathF.Pow(10, -sustainCb / 200.0f); // cb to dB, then dB to linear

            var voiceDef = new VoiceDefinition(format,
                oscType: Oscillator.WaveformType.Sine, // It's just a Placeholder, as this voice will use a sampler
                unison: 1,
                detune: 0,
                attack: TimecentsToSeconds(GenValue(GeneratorType.AttackVolEnv)),
                decay: TimecentsToSeconds(GenValue(GeneratorType.DecayVolEnv)),
                sustain: sustainLinear,
                release: TimecentsToSeconds(GenValue(GeneratorType.ReleaseVolEnv)),
                useFilter: GenValue(GeneratorType.InitialFilterFc, -1) != -1
            ) { Sample = sample };

            var keyRange = GetRangeValue(generators, GeneratorType.KeyRange);
            var velRange = GetRangeValue(generators, GeneratorType.VelRange);

            var mapping = new VoiceMapping(voiceDef)
            {
                MinKey = keyRange.Lo,
                MaxKey = keyRange.Hi,
                MinVelocity = velRange.Lo,
                MaxVelocity = velRange.Hi,
                InitialAttenuation = GenValue(GeneratorType.InitialAttenuation) / 10.0f, // 0.1 dB units
                Pan = GenValue(GeneratorType.Pan) * 0.1f / 50.0f, // tenths of a percent -> -1 to 1 range
                RootKeyOverride = GenValue(GeneratorType.OverridingRootKey, -1),
                Tune = GenValue(GeneratorType.CoarseTune) * 100 + GenValue(GeneratorType.FineTune),
                LoopMode = GenValue(GeneratorType.SampleModes)
            };

            return (voiceDef, mapping);

            // Helper to get a generator value or default
            short GenValue(GeneratorType type, short def = 0) => GetGeneratorValue(generators, type) ?? def;

            // Timecents are converted to seconds: time = 2^(timecents/1200)
            float TimecentsToSeconds(int timecents) => MathF.Pow(2.0f, timecents / 1200.0f);
        }

        private static List<GeneratorRecord> GetZoneGenerators(int bagIndex, BagChunk bags, GeneratorChunk generators)
        {
            var zoneGens = new List<GeneratorRecord>();
            if (bagIndex >= bags.Bags.Length) return zoneGens;

            var start = bags.Bags[bagIndex].GeneratorIndex;
            var end = (bagIndex + 1 < bags.Bags.Length)
                ? bags.Bags[bagIndex + 1].GeneratorIndex
                : generators.Generators.Length;

            for (var i = start; i < end; i++)
            {
                if (i < generators.Generators.Length) 
                    zoneGens.Add(generators.Generators[i]);
            }

            return zoneGens;
        }

        private static short? GetGeneratorValue(List<GeneratorRecord> generators, GeneratorType type)
        {
            // The last generator in the list for a given type overrides previous ones.
            for (var i = generators.Count - 1; i >= 0; i--)
            {
                if (generators[i].Operator == type)
                    return generators[i].Amount;
            }

            return null;
        }

        private static (byte Lo, byte Hi) GetRangeValue(List<GeneratorRecord> generators, GeneratorType type)
        {
            var val = GetGeneratorValue(generators, type);
            return val.HasValue ? ((byte)(val.Value & 0xFF), (byte)((val.Value >> 8) & 0xFF)) : ((byte Lo, byte Hi))(0, 127);
        }

        private static string GetString(byte[] nameBytes)
        {
            var terminator = Array.IndexOf(nameBytes, (byte)0);
            return Encoding.UTF8.GetString(nameBytes, 0, terminator > -1 ? terminator : nameBytes.Length);
        }

        /// <summary>
        /// Disposes the underlying file stream.
        /// </summary>
        public void Dispose()
        {
            _stream.Dispose();
        }
    }