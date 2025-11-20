using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace SoundFlow.Codecs.FFMpeg;

/// <summary>
/// An <see cref="ICodecFactory"/> that uses a native FFmpeg wrapper to provide decoding and encoding
/// for a wide range of audio formats. Register an instance of this factory with your
/// <see cref="AudioEngine"/> to enable support for additional codecs.
/// </summary>
public sealed class FFmpegCodecFactory : ICodecFactory
{
    /// <inheritdoc />
    public string FactoryId => "SoundFlow.Codecs.FFMpeg";

    /// <inheritdoc />
    /// <remarks>
    /// This list is derived from the specific FFmpeg build configuration, including enabled demuxers,
    /// decoders, and encoders. It maps common file extensions to the codecs available in the native library.
    /// </remarks>
    public IReadOnlyCollection<string> SupportedFormatIds { get; } = 
    [
        // Lossless Formats
        "wav",       
        "aiff",      
        "flac",      
        "alac",      
        "ape",       
        "wv",        
        "tta",       
        "shn",       
        
        // Lossy Formats
        "mp3",       
        "mp2",       
        "ogg",       
        "opus",      
        "aac",       
        "m4a",       
        "wma",       
        "ac3",       
        
        // Container Formats
        "mka",       
        "mpc",       
        "tak",       
        "ra",        
        "dsf",       
        "au",        
        "gsm"        
    ];

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public ISoundDecoder? CreateDecoder(Stream stream, string formatId, AudioFormat format)
    {
        return SupportedFormatIds.Contains(formatId.ToLowerInvariant()) 
            ? new FFmpegDecoder(stream, format) 
            : null;
    }

    /// <inheritdoc />
    public ISoundDecoder? TryCreateDecoder(Stream stream, out AudioFormat detectedFormat, AudioFormat? hintFormat = null)
    {
        detectedFormat = default;
        try
        {
            
            // use hint or a dummy format to initialize the decoder, but the actual format is determined by the underlying FFmpeg wrapper.
            var decoder = new FFmpegDecoder(stream, hintFormat ?? AudioFormat.DvdHq);

            // If initialization succeeds, the decoder has determined the actual format.
            detectedFormat = new AudioFormat
            {
                Format = decoder.SampleFormat,
                Channels = decoder.Channels,
                SampleRate = decoder.SampleRate,
                Layout = AudioFormat.GetLayoutFromChannels(decoder.Channels)
            };
            return decoder;
        }
        catch (Exception)
        {
            if (stream.CanSeek) 
                stream.Position = 0;
            return null;
        }
    }

    /// <inheritdoc />
    public ISoundEncoder? CreateEncoder(Stream stream, string formatId, AudioFormat format)
    {
        // The native wrapper handles resampling from the source format to what the encoder needs.
        var encoderName = formatId.ToLowerInvariant();

        if (encoderName == "m4a")
        {
            encoderName = format.Format is SampleFormat.S24 or SampleFormat.S32
                ? "alac"
                : "aac";
        }
        
        return SupportedFormatIds.Contains(formatId.ToLowerInvariant()) 
            ? new FFmpegEncoder(stream, encoderName, format) 
            : null;
    }
}