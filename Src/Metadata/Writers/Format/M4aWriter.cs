using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Utilities;
using SoundFlow.Metadata.Writers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Writers.Format;

internal class M4aWriter : ISoundFormatWriter
{
    private record AtomInfo(string Type, long Position, long Size);

    public async Task<Result> RemoveTagsAsync(string sourcePath, string destinationPath)
    {
        return await ProcessM4aFileAsync(sourcePath, destinationPath, null);
    }

    public async Task<Result> WriteTagsAsync(string sourcePath, string destinationPath, SoundTags tags)
    {
        return await ProcessM4aFileAsync(sourcePath, destinationPath, tags);
    }

    private async Task<Result> ProcessM4aFileAsync(string sourcePath, string destinationPath, SoundTags? tags)
    {
        try
        {
            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            
            var topLevelAtomsResult = MapTopLevelAtoms(sourceStream);
            if(topLevelAtomsResult.IsFailure || topLevelAtomsResult.Value is null) return topLevelAtomsResult;
            var topLevelAtoms = topLevelAtomsResult.Value;
            
            var moovAtomInfo = topLevelAtoms.FirstOrDefault(a => a.Type == "moov");
            if (moovAtomInfo == null)
                return new HeaderNotFoundError("moov atom");
            
            var mdatAtomInfo = topLevelAtoms.FirstOrDefault(a => a.Type == "mdat");
            if (mdatAtomInfo == null)
                return new HeaderNotFoundError("mdat atom");

            // Read the original 'moov' atom's data
            sourceStream.Position = moovAtomInfo.Position;
            using var moovReader = new BigEndianBinaryReader(sourceStream);
            var moovAtom = M4aAtom.ParseAtoms(moovReader, moovAtomInfo.Position + moovAtomInfo.Size).First();

            // Modify the 'moov' tree (add/remove tags)
            ModifyMoovTree(moovAtom, tags);
            
            var newMoovData = moovAtom.ToByteArray();
            var sizeDelta = newMoovData.Length - moovAtomInfo.Size;

            if (sizeDelta != 0 && mdatAtomInfo.Position > moovAtomInfo.Position)
            {
                var updateResult = UpdateChunkOffsets(moovAtom, sizeDelta);
                if(updateResult.IsFailure) return updateResult;
                newMoovData = moovAtom.ToByteArray();
            }

            // Write the new file
            await using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);

            foreach (var atomInfo in topLevelAtoms)
            {
                if (atomInfo.Type == "moov")
                {
                    await destStream.WriteAsync(newMoovData);
                }
                else
                {
                    sourceStream.Position = atomInfo.Position;
                    await CopyStreamBytesAsync(sourceStream, destStream, atomInfo.Size);
                }
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while processing the M4A file.", ex);
        }
    }

    private Result UpdateChunkOffsets(M4aAtom moovAtom, long delta)
    {
        try
        {
            // Find all 'stco' (32-bit chunk offset) atoms.
            var stcoAtoms = moovAtom.Children.Where(c => c.Type == "trak")
                .Select(t => t.FindDescendant("stco"))
                .Where(a => a is { Data.Length: >= 8 });

            foreach (var stco in stcoAtoms)
            {
                using var ms = new MemoryStream(stco!.Data!);
                using var reader = new BigEndianBinaryReader(ms);
                
                var versionAndFlags = reader.ReadBytes(4);
                var entryCount = reader.ReadUInt32();
                
                var newOffsets = new List<uint>();
                for (var i = 0; i < entryCount; i++)
                {
                    var originalOffset = reader.ReadUInt32();
                    newOffsets.Add((uint)(originalOffset + delta));
                }

                using var outMs = new MemoryStream();
                using var writer = new BigEndianBinaryWriter(outMs);
                writer.Write(versionAndFlags);
                writer.Write(entryCount);
                foreach (var offset in newOffsets) writer.Write(offset);
                stco.Data = outMs.ToArray();
            }
            
            // Find all 'co64' (64-bit chunk offset) atoms.
            var co64Atoms = moovAtom.Children.Where(c => c.Type == "trak")
               .Select(t => t.FindDescendant("co64"))
               .Where(a => a is { Data.Length: >= 8 });

            foreach (var co64 in co64Atoms)
            {
                using var ms = new MemoryStream(co64!.Data!);
                using var reader = new BigEndianBinaryReader(ms);

                var versionAndFlags = reader.ReadBytes(4);
                var entryCount = reader.ReadUInt32();

                var newOffsets = new List<ulong>();
                for (var i = 0; i < entryCount; i++)
                {
                    var originalOffset = reader.ReadUInt64();
                    newOffsets.Add((ulong)((long)originalOffset + delta));
                }
                
                using var outMs = new MemoryStream();
                using var writer = new BigEndianBinaryWriter(outMs);
                writer.Write(versionAndFlags);
                writer.Write(entryCount);
                foreach (var offset in newOffsets) writer.Write(offset);
                co64.Data = outMs.ToArray();
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new CorruptChunkError("stco/co64", "Chunk offset atom is malformed.", ex);
        }
    }

    private void ModifyMoovTree(M4aAtom moovAtom, SoundTags? tags)
    {
        var udtaAtom = moovAtom.Children.FirstOrDefault(a => a.Type == "udta");

        if (tags != null) // Write or update tags
        {
            if (udtaAtom == null)
            {
                udtaAtom = new M4aAtom("udta");
                moovAtom.Children.Add(udtaAtom);
            }

            var metaAtom = udtaAtom.Children.FirstOrDefault(a => a.Type == "meta");
            if (metaAtom == null)
            {
                metaAtom = new M4aAtom("meta") { Data = new byte[4] }; // 4-byte null version/flags
                udtaAtom.Children.Add(metaAtom);
            }

            if (!metaAtom.Children.Exists(a => a.Type == "hdlr"))
            {
                var hdlrAtom = new M4aAtom("hdlr")
                {
                    Data = Convert.FromHexString("00000000000000006d69726170706c000000000000000000")
                };
                metaAtom.Children.Insert(0, hdlrAtom);
            }
            
            metaAtom.Children.RemoveAll(a => a.Type == "ilst");
            metaAtom.Children.Add(M4aTagBuilder.BuildIlstAtom(tags));
        }
        else // Remove tags
        {
            if (udtaAtom != null)
            {
                moovAtom.Children.Remove(udtaAtom);
            }
        }
    }
    
    private Result<List<AtomInfo>> MapTopLevelAtoms(Stream stream)
    {
        var atoms = new List<AtomInfo>();
        stream.Position = 0;
        
        try
        {
            var reader = new BigEndianBinaryReader(stream);
            while (stream.Position < stream.Length)
            {
                var atomPosition = stream.Position;
                if (stream.Length - atomPosition < 8) break;

                long size = reader.ReadUInt32();
                var type = reader.ReadString(4);
                
                if (size == 1)
                {
                    if (stream.Length - atomPosition < 16) break;
                    size = reader.ReadInt64();
                }
                if (size == 0)
                {
                    size = stream.Length - atomPosition;
                }
                if (size < 8)
                    return new CorruptChunkError(type, "Atom size is invalid (less than 8 bytes).");
                
                atoms.Add(new AtomInfo(type, atomPosition, size));
                stream.Position = atomPosition + size;
            }
            return atoms;
        }
        catch (EndOfStreamException ex)
        {
            return new CorruptChunkError("Atom", "File is truncated or an atom size is incorrect.", ex);
        }
    }
    
    private async Task CopyStreamBytesAsync(Stream source, Stream destination, long count)
    {
        var buffer = new byte[81920];
        var remaining = count;
        while (remaining > 0)
        {
            var bytesToRead = (int)Math.Min(remaining, buffer.Length);
            var bytesRead = await source.ReadAsync(buffer.AsMemory(0, bytesToRead));
            if (bytesRead == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
            remaining -= bytesRead;
        }
    }
}