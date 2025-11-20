namespace SoundFlow.Metadata.SoundFont;

/// <summary>
/// Defines the standard SF2 generator types as an enumeration.
/// These operators control various aspects of a voice's behavior.
/// </summary>
public enum GeneratorType : ushort
{
    /// <summary>
    /// Sample start address offset (fine adjustment in sample points).
    /// </summary>
    StartAddrsOffset = 0,
    /// <summary>
    /// Sample end address offset (fine adjustment in sample points).
    /// </summary>
    EndAddrsOffset = 1,
    /// <summary>
    /// Sample loop start address offset (fine adjustment in sample points).
    /// </summary>
    StartLoopAddrsOffset = 2,
    /// <summary>
    /// Sample loop end address offset (fine adjustment in sample points).
    /// </summary>
    EndLoopAddrsOffset = 3,
    /// <summary>
    /// Sample start address coarse offset (coarse adjustment in blocks of 32768 samples).
    /// </summary>
    StartAddrsCoarseOffset = 4,
    /// <summary>
    /// Modulation LFO to Pitch depth.
    /// </summary>
    ModLfoToPitch = 5,
    /// <summary>
    /// Vibrato LFO to Pitch depth.
    /// </summary>
    VibLfoToPitch = 6,
    /// <summary>
    /// Modulation Envelope to Pitch depth.
    /// </summary>
    ModEnvToPitch = 7,
    /// <summary>
    /// Initial value for Low Pass Filter Cutoff Frequency (in cents).
    /// </summary>
    InitialFilterFc = 8,
    /// <summary>
    /// Initial value for Low Pass Filter Q (resonance).
    /// </summary>
    InitialFilterQ = 9,
    /// <summary>
    /// Modulation LFO to Filter Cutoff depth.
    /// </summary>
    ModLfoToFilterFc = 10,
    /// <summary>
    /// Modulation Envelope to Filter Cutoff depth.
    /// </summary>
    ModEnvToFilterFc = 11,
    /// <summary>
    /// Sample end address coarse offset (coarse adjustment in blocks of 32768 samples).
    /// </summary>
    EndAddrsCoarseOffset = 12,
    /// <summary>
    /// Modulation LFO to Volume depth.
    /// </summary>
    ModLfoToVolume = 13,
    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Unused1 = 14,
    /// <summary>
    /// Chorus effects send amount.
    /// </summary>
    ChorusEffectsSend = 15,
    /// <summary>
    /// Reverb effects send amount.
    /// </summary>
    ReverbEffectsSend = 16,
    /// <summary>
    /// Stereo panning (center is 0).
    /// </summary>
    Pan = 17,
    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Unused2 = 18,
    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Unused3 = 19,
    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Unused4 = 20,
    /// <summary>
    /// Delay time for the Modulation LFO.
    /// </summary>
    DelayModLFO = 21,
    /// <summary>
    /// Frequency (rate) of the Modulation LFO.
    /// </summary>
    FreqModLFO = 22,
    /// <summary>
    /// Delay time for the Vibrato LFO.
    /// </summary>
    DelayVibLFO = 23,
    /// <summary>
    /// Frequency (rate) of the Vibrato LFO.
    /// </summary>
    FreqVibLFO = 24,
    /// <summary>
    /// Delay time for the Modulation Envelope.
    /// </summary>
    DelayModEnv = 25,
    /// <summary>
    /// Attack time for the Modulation Envelope.
    /// </summary>
    AttackModEnv = 26,
    /// <summary>
    /// Hold time for the Modulation Envelope.
    /// </summary>
    HoldModEnv = 27,
    /// <summary>
    /// Decay time for the Modulation Envelope.
    /// </summary>
    DecayModEnv = 28,
    /// <summary>
    /// Sustain level for the Modulation Envelope.
    /// </summary>
    SustainModEnv = 29,
    /// <summary>
    /// Release time for the Modulation Envelope.
    /// </summary>
    ReleaseModEnv = 30,
    /// <summary>
    /// Keyboard key number to Modulation Envelope Hold scaling.
    /// </summary>
    KeyNumToModEnvHold = 31,
    /// <summary>
    /// Keyboard key number to Modulation Envelope Decay scaling.
    /// </summary>
    KeyNumToModEnvDecay = 32,
    /// <summary>
    /// Delay time for the Volume Envelope.
    /// </summary>
    DelayVolEnv = 33,
    /// <summary>
    /// Attack time for the Volume Envelope.
    /// </summary>
    AttackVolEnv = 34,
    /// <summary>
    /// Hold time for the Volume Envelope.
    /// </summary>
    HoldVolEnv = 35,
    /// <summary>
    /// Decay time for the Volume Envelope.
    /// </summary>
    DecayVolEnv = 36,
    /// <summary>
    /// Sustain level for the Volume Envelope.
    /// </summary>
    SustainVolEnv = 37,
    /// <summary>
    /// Release time for the Volume Envelope.
    /// </summary>
    ReleaseVolEnv = 38,
    /// <summary>
    /// Keyboard key number to Volume Envelope Hold scaling.
    /// </summary>
    KeyNumToVolEnvHold = 39,
    /// <summary>
    /// Keyboard key number to Volume Envelope Decay scaling.
    /// </summary>
    KeyNumToVolEnvDecay = 40,
    /// <summary>
    /// Specifies the index of the Instrument this preset zone refers to.
    /// </summary>
    Instrument = 41,
    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Reserved1 = 42,
    /// <summary>
    /// Specifies the MIDI key range this zone responds to.
    /// </summary>
    KeyRange = 43,
    /// <summary>
    /// Specifies the MIDI velocity range this zone responds to.
    /// </summary>
    VelRange = 44,
    /// <summary>
    /// Sample loop start address coarse offset.
    /// </summary>
    StartLoopAddrsCoarseOffset = 45,
    /// <summary>
    /// Specifies the fixed MIDI key number to use if this zone is not key-mapped.
    /// </summary>
    KeyNum = 46,
    /// <summary>
    /// Specifies the fixed MIDI velocity to use if this zone is not velocity-mapped.
    /// </summary>
    Velocity = 47,
    /// <summary>
    /// Initial volume attenuation (in centibels).
    /// </summary>
    InitialAttenuation = 48,
    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Reserved2 = 49,
    /// <summary>
    /// Sample loop end address coarse offset.
    /// </summary>
    EndLoopAddrsCoarseOffset = 50,
    /// <summary>
    /// Coarse tuning adjustment (in semitones).
    /// </summary>
    CoarseTune = 51,
    /// <summary>
    /// Fine tuning adjustment (in cents).
    /// </summary>
    FineTune = 52,
    /// <summary>
    /// Specifies the index of the Sample Header this instrument zone refers to.
    /// </summary>
    SampleID = 53,
    /// <summary>
    /// Specifies the loop behavior (e.g., no loop, continuous loop).
    /// </summary>
    SampleModes = 54,
    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Reserved3 = 55,
    /// <summary>
    /// Tuning ratio applied to the keyboard (default is 100%).
    /// </summary>
    ScaleTuning = 56,
    /// <summary>
    /// Used for exclusive voice handling (e.g., hi-hat closed/open).
    /// </summary>
    ExclusiveClass = 57,
    /// <summary>
    /// Overrides the Sample Header's root key.
    /// </summary>
    OverridingRootKey = 58,
    /// <summary>
    /// Reserved for future use.
    /// </summary>
    Unused5 = 59,
    /// <summary>
    /// Marker to indicate the end of the generator list for a zone.
    /// </summary>
    EndOper = 60
}