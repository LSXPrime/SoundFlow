using Microsoft.Win32.SafeHandles;

namespace SoundFlow.Codecs.FFMpeg.Native;

/// <summary>
/// Represents a wrapper for a native SF_Decoder handle.
/// </summary>
internal sealed class SafeDecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SafeDecoderHandle"/> class.
    /// </summary>
    public SafeDecoderHandle() : base(true) { }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        FFmpeg.FreeDecoder(handle);
        return true;
    }
}

/// <summary>
/// Represents a wrapper for a native SF_Encoder handle.
/// </summary>
internal sealed class SafeEncoderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SafeEncoderHandle"/> class.
    /// </summary>
    public SafeEncoderHandle() : base(true) { }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        // The native implementation flushes the encoder within the free function.
        FFmpeg.FreeEncoder(handle);
        return true;
    }
}