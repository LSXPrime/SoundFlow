using System.Text;

namespace SoundFlow.Metadata.Utilities;

internal enum AudioFormatType
{
    Unsupported,
    Wav,
    Aiff,
    Flac,
    Ogg,
    Mp3,
    M4a,
    Aac
}

/// <summary>
/// A shared helper to identify audio formats from file headers.
/// </summary>
internal static class FormatIdentifier
{
    public static AudioFormatType Identify(byte[] header)
    {
        if (header.Length < 12) return AudioFormatType.Unsupported;

        // Container formats
        if (Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WAVE")
            return AudioFormatType.Wav;
        if (Encoding.ASCII.GetString(header, 0, 4) == "FORM" && (Encoding.ASCII.GetString(header, 8, 4) == "AIFF" ||
                                                                 Encoding.ASCII.GetString(header, 8, 4) == "AIFC"))
            return AudioFormatType.Aiff;
        if (Encoding.ASCII.GetString(header, 0, 4) == "fLaC") return AudioFormatType.Flac;
        if (Encoding.ASCII.GetString(header, 0, 4) == "OggS") return AudioFormatType.Ogg;
        if (Encoding.ASCII.GetString(header, 4, 4) == "ftyp") return AudioFormatType.M4a; // M4A/MP4

        // Frame-based formats (no global header)

        var isMp3 = header[0] == 0xFF && (header[1] & 0xE0) == 0xE0 && ((header[1] >> 1) & 0x03) != 0;
        var isAac = header[0] == 0xFF && (header[1] & 0xF0) == 0xF0;

        // If it matches the more general AAC sync word, we must ensure it's not actually an MP3.
        if (isMp3) return AudioFormatType.Mp3;
        if (isAac) return AudioFormatType.Aac;

        return AudioFormatType.Unsupported;
    }
}