using System.Runtime.InteropServices;

namespace SoundFlow.Metadata.SoundFont;

#pragma warning disable CS0649 // Field is never assigned to

/// <summary>
/// Maps to the 'phdr' chunk record, defining a preset.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct PresetRecord
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] Name;
    public ushort Preset;
    public ushort Bank;
    public ushort PresetBagIndex;
    public uint Library;
    public uint Genre;
    public uint Morphology;
}

/// <summary>
/// Maps to the 'ibag' and 'pbag' chunk records, defining a zone.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct BagRecord
{
    public ushort GeneratorIndex;
    public ushort ModulatorIndex;
}

/// <summary>
/// Maps to the 'igen' and 'pgen' chunk records, defining a generator.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GeneratorRecord
{
    public GeneratorType Operator;
    public short Amount;
}

/// <summary>
/// Maps to the 'inst' chunk record, defining an instrument.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct InstrumentRecord
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] Name;
    public ushort InstrumentBagIndex;
}

/// <summary>
/// Maps to the 'shdr' chunk record, defining a sample.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct SampleHeaderRecord
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] Name;
    public uint Start;
    public uint End;
    public uint StartLoop;
    public uint EndLoop;
    public uint SampleRate;
    public byte OriginalKey;
    public sbyte Correction;
    public ushort SampleLink;
    public ushort SampleType;
}

#pragma warning restore CS0649