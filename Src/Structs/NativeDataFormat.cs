using System.Runtime.InteropServices;
using SoundFlow.Enums;

namespace SoundFlow.Structs;

/// <summary>
/// Represents a native data format with audio format, channels, sample rate, and flags.
/// </summary>
/// <remarks>
/// This struct is used to get the supported native data format of an audio device using the MiniAudio library.
/// Can be used by reading an array from a pointer to `NativeDataFormats` of <see cref="SoundFlow.Structs.DeviceInfo"/> struct.
/// </remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct NativeDataFormat
{
    /// <summary>
    /// The audio sample format.
    /// </summary>
    public SampleFormat Format;

    /// <summary>
    /// The number of audio channels.
    /// </summary>
    public uint Channels;

    /// <summary>
    /// The sample rate of the audio in Hz.
    /// </summary>
    public uint SampleRate;

    /// <summary>
    /// Additional flags for the native data format.
    /// </summary>
    public uint Flags;
}
