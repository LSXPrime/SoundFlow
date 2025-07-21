using System.Runtime.InteropServices;

namespace SoundFlow.Backends.MiniAudio.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfAlsaConfig
{
    [MarshalAs(UnmanagedType.U4)] public uint NoMMap;
    [MarshalAs(UnmanagedType.U4)] public uint NoAutoFormat;
    [MarshalAs(UnmanagedType.U4)] public uint NoAutoChannels;
    [MarshalAs(UnmanagedType.U4)] public uint NoAutoResample;
}