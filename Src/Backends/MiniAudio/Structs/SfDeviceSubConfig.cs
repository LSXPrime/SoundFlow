using System.Runtime.InteropServices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Enums;

namespace SoundFlow.Backends.MiniAudio.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfDeviceSubConfig
{
    public SampleFormat Format;
    public uint Channels;
    public nint pDeviceID;
    public ShareMode ShareMode;
}