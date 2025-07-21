using System.Runtime.InteropServices;

namespace SoundFlow.Structs;

/// <summary>
/// Represents device information including ID, name, default status, and data formats.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public record struct DeviceInfo
{
    /// <summary>
    /// The unique identifier for the device.
    /// </summary>
    public IntPtr Id;

    /// <summary>
    /// The name of the device.
    /// </summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]  // 255 + 1 for null terminator
    public string Name;

    /// <summary>
    /// Indicates whether the device is set as default.
    /// </summary>
    [MarshalAs(UnmanagedType.U1)]
    public bool IsDefault;

    /// <summary>
    /// The count of native data formats supported by the device.
    /// </summary>
    public uint NativeDataFormatCount;

    /// <summary>
    /// Pointer to the native data formats.
    /// </summary>
    public IntPtr NativeDataFormats;
}
