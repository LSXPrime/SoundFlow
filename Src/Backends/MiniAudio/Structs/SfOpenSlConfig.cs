using System.Runtime.InteropServices;
using SoundFlow.Backends.MiniAudio.Enums;

namespace SoundFlow.Backends.MiniAudio.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfOpenSlConfig
{
    public OpenSlStreamType StreamType;
    public OpenSlRecordingPreset RecordingPreset;
}