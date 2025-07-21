using System.Runtime.InteropServices;

namespace SoundFlow.Backends.MiniAudio.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SfDeviceConfig
{
    public uint PeriodSizeInFrames;
    public uint PeriodSizeInMilliseconds;
    public uint Periods;
    [MarshalAs(UnmanagedType.U1)] public bool NoPreSilencedOutputBuffer;
    [MarshalAs(UnmanagedType.U1)] public bool NoClip;
    [MarshalAs(UnmanagedType.U1)] public bool NoDisableDenormals;
    [MarshalAs(UnmanagedType.U1)] public bool NoFixedSizedCallback;
        
    public nint Playback;
    public nint Capture;
    public nint Wasapi;
    public nint CoreAudio;
    public nint Alsa;
    public nint Pulse;
    public nint OpenSL;
    public nint AAudio;
}