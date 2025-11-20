using System.Runtime.InteropServices;

namespace SoundFlow.Backends.MiniAudio.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct DeviceInfoNative
{
    public IntPtr Id;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
    public byte[] NameBytes;

    [MarshalAs(UnmanagedType.U1)]
    public bool IsDefault;

    public uint NativeDataFormatCount;

    public IntPtr NativeDataFormats;
}