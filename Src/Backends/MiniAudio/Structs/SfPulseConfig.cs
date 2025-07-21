using System.Runtime.InteropServices;

namespace SoundFlow.Backends.MiniAudio.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfPulseConfig
{
    public nint pStreamNamePlayback;
    public nint pStreamNameCapture;
}