using System.Runtime.InteropServices;
using SoundFlow.Backends.MiniAudio.Enums;

namespace SoundFlow.Backends.MiniAudio.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfAAudioConfig
{
    public AAudioUsage Usage;
    public AAudioContentType ContentType;
    public AAudioInputPreset InputPreset;
    public AAudioAllowedCapturePolicy AllowedCapturePolicy;
}