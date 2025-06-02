using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Exceptions;
using SoundFlow.Interfaces;

namespace SoundFlow.Backends.MiniAudio;

/// <summary>
/// An object to assist with encoding raw PCM frames into audio formats.
/// </summary>
internal sealed unsafe class MiniAudioEncoder : ISoundEncoder
{
    private readonly nint _encoder;
    public string FilePath { get; }

    /// <summary>
    /// Constructs a new encoder to write to the given file in the specified format.
    /// </summary>
    /// <param name="filePath">The path to the file to write encoded audio to.</param>
    /// <param name="encodingFormat">The desired audio encoding format.</param>
    /// <param name="sampleFormat">The format of the input audio samples.</param>
    /// <param name="channels">The number of audio channels.</param>
    /// <param name="sampleRate">The sample rate of the input audio.</param>
    public MiniAudioEncoder(string filePath, EncodingFormat encodingFormat, SampleFormat sampleFormat, int channels,
        int sampleRate)
    {
        if (encodingFormat != EncodingFormat.Wav)
            throw new NotSupportedException("MiniAudio only supports WAV encoding.");
        
        FilePath = filePath;

        // Construct encoder config
        var config = Native.AllocateEncoderConfig(encodingFormat, sampleFormat, (uint)channels, (uint)sampleRate);

        // Allocate encoder and initialize
        _encoder = Native.AllocateEncoder();
        var result = Native.EncoderInitFile(filePath, config, _encoder);
        if (result != Result.Success)
            throw new BackendException("MiniAudio", result, "Unable to initialize encoder.");
    }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Encodes the given samples and writes them to the output file.
    /// </summary>
    /// <param name="samples">The buffer containing the PCM samples to encode.</param>
    /// <returns>The number of samples successfully encoded.</returns>
    public int Encode(Span<float> samples)
    {
        var framesToWrite = (ulong)(samples.Length / AudioEngine.Channels);
        ulong framesWritten = 0;

        fixed (float* pSamples = samples)
        {
            var result = Native.EncoderWritePcmFrames(_encoder, (nint)pSamples, framesToWrite, &framesWritten);
            if (result != Result.Success)
                throw new BackendException("MiniAudio", result, "Failed to write PCM frames to encoder.");
        }

        return (int)framesWritten * AudioEngine.Channels;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MiniAudioEncoder()
    {
        Dispose(false);
    }

    private void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            if (disposing)
                Native.EncoderUninit(_encoder);
            
            Native.Free(_encoder);

            IsDisposed = true;
        }
    }
}