using System.Text;
using SoundFlow.Utils;

namespace SoundFlow.Metadata.SoundFont;

/// <summary>
/// Manages the extraction, conversion, and resampling of all audio samples
/// from the 'sdta' chunk of an SF2 file.
/// </summary>
internal sealed class SoundFontSampleProvider
{
    private record RiffChunk(string Id, uint Size, long Position);
    public readonly List<SampleData> Samples = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SoundFontSampleProvider"/> class.
    /// </summary>
    /// <param name="stream">The file stream of the SF2 file.</param>
    /// <param name="sampleHeaders">The parsed sample header chunk.</param>
    /// <param name="targetSampleRate">The target sample rate of the audio engine.</param>
    /// <exception cref="InvalidDataException">Thrown if the 'sdta' or 'smpl' chunk is missing or malformed.</exception>
    public SoundFontSampleProvider(Stream stream, SampleHeaderChunk sampleHeaders, int targetSampleRate)
    {
        // Find the 'smpl' sub-chunk within the 'sdta' LIST chunk.
        var smplChunkBytes = ReadSmplChunk(stream);
        
        // The last sample header in an SF2 file is a mandatory terminator and should be ignored.
        for (var i = 0; i < sampleHeaders.SampleHeaders.Length - 1; i++)
        {
            var header = sampleHeaders.SampleHeaders[i];
            
            if (header.End <= header.Start)
            {
                Samples.Add(new SampleData { Name = GetString(header.Name) }); // Add empty for placeholder
                continue;
            }

            var sampleLength = (int)(header.End - header.Start);
            var byteOffset = (int)header.Start * 2;
            var byteCount = sampleLength * 2;

            if (byteOffset + byteCount > smplChunkBytes.Length)
            {
                // Handle corrupt SF2 where sample header points outside the data chunk.
                Samples.Add(new SampleData { Name = GetString(header.Name) });
                continue;
            }

            var shortData = new short[sampleLength];
            Buffer.BlockCopy(smplChunkBytes, byteOffset, shortData, 0, byteCount);

            var floatData = new float[sampleLength];
            for(var j = 0; j < sampleLength; j++)
            {
                floatData[j] = shortData[j] / 32768.0f;
            }

            var resampleRatio = (double)targetSampleRate / header.SampleRate;

            var resampledData = header.SampleRate > 0 && Math.Abs(resampleRatio - 1.0) > 1e-6
                ? MathHelper.ResampleLinear(floatData, 1, (int)header.SampleRate, targetSampleRate)
                : floatData;

            var sampleData = new SampleData
            {
                Name = GetString(header.Name),
                Data = resampledData,
                StartLoop = (uint)((header.StartLoop - header.Start) * resampleRatio),
                EndLoop = (uint)((header.EndLoop - header.Start) * resampleRatio),
                OriginalSampleRate = header.SampleRate,
                RootKey = header.OriginalKey,
                Correction = header.Correction
            };
            Samples.Add(sampleData);
        }
    }
    
    /// <summary>
    /// Finds and reads the entire 'smpl' data chunk into a byte array.
    /// </summary>
    private static byte[] ReadSmplChunk(Stream stream)
    {
        stream.Position = 12; // After RIFF header
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);

        // Find the 'sdta' LIST chunk first
        while (stream.Position < stream.Length - 8)
        {
            var chunk = ReadChunkHeader(reader);
            if (chunk.Id == "LIST")
            {
                var listType = new string(reader.ReadChars(4));
                if (listType == "sdta")
                {
                    // Now we are inside the 'sdta' chunk, find the 'smpl' sub-chunk
                    var sdtaEnd = stream.Position + chunk.Size - 4;
                    while (stream.Position < sdtaEnd)
                    {
                        var subChunk = ReadChunkHeader(reader);
                        if (subChunk.Id == "smpl")
                            return reader.ReadBytes((int)subChunk.Size);
                        stream.Position = subChunk.Position + 8 + subChunk.Size;
                    }
                }
            }
             // Move to the next chunk
            stream.Position = chunk.Position + 8 + chunk.Size;
        }

        throw new InvalidDataException("Could not find 'sdta' LIST chunk containing 'smpl' data.");
    }

    private static RiffChunk ReadChunkHeader(BinaryReader reader)
    {
        var pos = reader.BaseStream.Position;
        var id = new string(reader.ReadChars(4));
        var size = reader.ReadUInt32();
        
        // RIFF chunks are padded to an even number of bytes, the size field does NOT include the pad byte
        var alignedSize = size % 2 != 0 ? size + 1 : size;
        return new RiffChunk(id, alignedSize, pos);
    }
    
    private static string GetString(byte[] nameBytes)
    {
        var terminator = Array.IndexOf(nameBytes, (byte)0);
        return Encoding.ASCII.GetString(nameBytes, 0, terminator > -1 ? terminator : nameBytes.Length);
    }
}