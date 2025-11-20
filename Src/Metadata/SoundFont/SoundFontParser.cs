using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.SoundFont;

/// <summary>
/// A parser for reading the metadata structures of a SoundFont 2 file.
/// </summary>
internal static class SoundFontParser
{
    private record RiffChunk(string Id, uint Size, long Position);

    /// <summary>
    /// Parses the metadata from an SF2 file stream.
    /// </summary>
    /// <param name="stream">The stream of the SF2 file.</param>
    /// <returns>A ParsedSoundFont object containing the structured metadata.</returns>
    /// <exception cref="InvalidDataException">Thrown if the file is not a valid SF2 file or is malformed.</exception>
    public static ParsedSoundFont Parse(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        // RIFF Header
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a RIFF file.");
        reader.ReadUInt32(); // File size
        if (new string(reader.ReadChars(4)) != "sfbk") throw new InvalidDataException("Not a SoundFont bank file.");

        var parsedData = new ParsedSoundFont();

        // Iterate through top-level chunks to find the 'pdta' LIST chunk
        while (stream.Position < stream.Length - 8)
        {
            var chunk = ReadChunkHeader(reader);
            if (chunk.Id == "LIST")
            {
                var listType = new string(reader.ReadChars(4));
                if (listType == "pdta")
                {
                    ParsePdtaSubChunks(reader, stream.Position + chunk.Size - 4, parsedData);
                    // We've parsed what we need from pdta, we can break
                    break;
                }
            }
            // Move to the next chunk
            stream.Position = chunk.Position + 8 + chunk.Size;
        }

        if (parsedData.Presets == null || parsedData.Instruments == null || parsedData.SampleHeaders == null)
        {
            throw new InvalidDataException("SoundFont file is missing one or more essential pdta sub-chunks.");
        }

        return parsedData;
    }

    /// <summary>
    /// Parses the sub-chunks within the 'pdta' LIST chunk.
    /// </summary>
    private static void ParsePdtaSubChunks(BinaryReader reader, long pdtaEnd, ParsedSoundFont parsedData)
    {
        while (reader.BaseStream.Position < pdtaEnd)
        {
            var subChunk = ReadChunkHeader(reader);
            var nextChunkPos = reader.BaseStream.Position + subChunk.Size;

            switch (subChunk.Id)
            {
                case "phdr": parsedData.Presets = new PresetChunk(ReadRecords<PresetRecord>(reader, subChunk.Size)); break;
                case "pbag": parsedData.PresetBags = new BagChunk(ReadRecords<BagRecord>(reader, subChunk.Size)); break;
                case "pgen": parsedData.PresetGenerators = new GeneratorChunk(ReadRecords<GeneratorRecord>(reader, subChunk.Size)); break;
                case "inst": parsedData.Instruments = new InstrumentChunk(ReadRecords<InstrumentRecord>(reader, subChunk.Size)); break;
                case "ibag": parsedData.InstrumentBags = new BagChunk(ReadRecords<BagRecord>(reader, subChunk.Size)); break;
                case "igen": parsedData.InstrumentGenerators = new GeneratorChunk(ReadRecords<GeneratorRecord>(reader, subChunk.Size)); break;
                case "shdr": parsedData.SampleHeaders = new SampleHeaderChunk(ReadRecords<SampleHeaderRecord>(reader, subChunk.Size)); break;
            }

            // Seek to the next chunk, ensuring alignment
            reader.BaseStream.Position = nextChunkPos;
        }
    }

    private static T[] ReadRecords<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] T>(BinaryReader reader, uint chunkSize) where T : struct
    {
        var recordSize = Marshal.SizeOf<T>();
        if (recordSize == 0 || chunkSize % recordSize != 0)
            throw new InvalidDataException($"Chunk size {chunkSize} is not a multiple of record size {recordSize} for type {typeof(T).Name}.");
        
        var recordCount = (int)(chunkSize / recordSize);
        var records = new T[recordCount];
        var bytes = reader.ReadBytes((int)chunkSize);

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var ptr = handle.AddrOfPinnedObject();
            for (var i = 0; i < recordCount; i++)
            {
                records[i] = Marshal.PtrToStructure<T>(ptr);
                ptr += recordSize;
            }
        }
        finally
        {
            handle.Free();
        }

        return records;
    }

    private static RiffChunk ReadChunkHeader(BinaryReader reader)
    {
        var pos = reader.BaseStream.Position;
        var id = new string(reader.ReadChars(4));
        var size = reader.ReadUInt32();
        // RIFF chunks are padded to an even number of bytes
        if (size % 2 != 0) size++;
        
        return new RiffChunk(id, size, pos);
    }
}