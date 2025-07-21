namespace SoundFlow.Enums;

/// <summary>
/// Enum for sample formats.
/// </summary>
/// <remarks>
/// Currently only contains standard formats.
/// </remarks>
public enum SampleFormat
{
    /// <summary>
    /// Unknown sample format.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Unsigned 8-bit format.
    /// </summary>
    U8 = 1,

    /// <summary>
    /// Signed 16-bit format.
    /// </summary>
    S16 = 2,

    /// <summary>
    /// Signed 24-bit format.
    /// </summary>
    S24 = 3,

    /// <summary>
    /// Signed 32-bit format.
    /// </summary>
    S32 = 4,

    /// <summary>
    /// 32-bit floating point format.
    /// </summary>
    F32 = 5
}
