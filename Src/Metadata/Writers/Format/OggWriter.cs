using System.Text;
using SoundFlow.Metadata.Abstracts;
using SoundFlow.Metadata.Models;
using SoundFlow.Metadata.Utilities;
using SoundFlow.Metadata.Writers.Tags;
using SoundFlow.Structs;

namespace SoundFlow.Metadata.Writers.Format;

internal class OggWriter : ISoundFormatWriter
{
    public async Task<Result> RemoveTagsAsync(string sourcePath, string destinationPath)
    {
        return await ProcessOggFileAsync(sourcePath, destinationPath, null);
    }

    public async Task<Result> WriteTagsAsync(string sourcePath, string destinationPath, SoundTags tags)
    {
        return await ProcessOggFileAsync(sourcePath, destinationPath, tags);
    }

    private async Task<Result> ProcessOggFileAsync(string sourcePath, string destinationPath, SoundTags? tags)
    {
        try
        {
            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            await using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);

            var allPages = new List<OggPage>();
            while (sourceStream.Position < sourceStream.Length)
            {
                var pageResult = await OggPage.FromStreamAsync(sourceStream);
                if(pageResult.IsFailure) return pageResult;
                var page = pageResult.Value;
                if (page == null) break;
                allPages.Add(page);
            }

            if (allPages.Count == 0)
                return new HeaderNotFoundError("Ogg Page");

            var headerPackets = new List<byte[]>();
            var audioPages = new List<OggPage>();
            var codecType = "unknown";

            foreach (var page in allPages)
            {
                if (page.GranulePosition == 0 && audioPages.Count == 0)
                {
                    foreach (var packet in page.Packets.Where(p => p.Length > 0))
                    {
                        if (headerPackets.Count == 0) 
                        {
                            if (packet is [0x01, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s', ..])
                                codecType = "vorbis";
                            else if (packet.Length >= 8 && Encoding.ASCII.GetString(packet, 0, 8) == "OpusHead")
                                codecType = "opus";
                        }

                        var isCommentPacket = (codecType == "vorbis" && packet[0] == 0x03) ||
                                              (codecType == "opus" && packet.Length > 8 && Encoding.ASCII.GetString(packet, 0, 8) == "OpusTags");

                        if (!isCommentPacket) headerPackets.Add(packet);
                    }
                }
                else
                {
                    audioPages.Add(page);
                }
            }
            
            if (headerPackets.Count == 0)
                return new CorruptChunkError("Ogg Stream", "Could not find any valid header packets.");

            var serialNumber = allPages.First().SerialNumber;
            uint pageSequence = 0;

            var firstHeaderPage = new OggPage(headerPackets.First(), serialNumber, pageSequence++, 0, 0x02); // BOS
            await destStream.WriteAsync(firstHeaderPage.ToByteArray());
            
            if (tags != null)
            {
                byte[] commentPacket;
                if (codecType == "opus")
                {
                    var commentData = VorbisCommentBuilder.Build(tags);
                    var opusTagsId = "OpusTags"u8.ToArray();
                    commentPacket = new byte[opusTagsId.Length + commentData.Length];
                    Buffer.BlockCopy(opusTagsId, 0, commentPacket, 0, opusTagsId.Length);
                    Buffer.BlockCopy(commentData, 0, commentPacket, opusTagsId.Length, commentData.Length);
                }
                else // Assume Vorbis as default
                {
                    var commentData = VorbisCommentBuilder.Build(tags);
                    var vorbisId = "vorbis"u8.ToArray();
                    commentPacket = new byte[1 + vorbisId.Length + commentData.Length];
                    commentPacket[0] = 0x03; // Type 3: Comment Header
                    Buffer.BlockCopy(vorbisId, 0, commentPacket, 1, vorbisId.Length);
                    Buffer.BlockCopy(commentData, 0, commentPacket, 1 + vorbisId.Length, commentData.Length);
                }
                var newCommentPage = new OggPage(commentPacket, serialNumber, pageSequence++, 0, 0x00);
                await destStream.WriteAsync(newCommentPage.ToByteArray());
            }

            foreach (var headerPacket in headerPackets.Skip(1))
            {
                var setupPage = new OggPage(headerPacket, serialNumber, pageSequence++, 0, 0x00);
                await destStream.WriteAsync(setupPage.ToByteArray());
            }

            for (var i = 0; i < audioPages.Count; i++)
            {
                var audioPage = audioPages[i];
                audioPage.PageSequenceNumber = pageSequence++;
                audioPage.HeaderType &= 0xFB; // Clear any existing EOS flag
                if (i == audioPages.Count - 1) audioPage.HeaderType |= 0x04; // Set EOS on the last page

                await destStream.WriteAsync(audioPage.ToByteArray());
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while processing the Ogg file.", ex);
        }
    }

    private class OggPage
    {
        public byte Version { get; init; }
        public byte HeaderType { get; set; }
        public long GranulePosition { get; private init; }
        public uint SerialNumber { get; private init; }
        public uint PageSequenceNumber { get; set; }
        public List<byte[]> Packets { get; } = [];

        private OggPage()
        {
        }

        public OggPage(byte[] packet, uint serialNumber, uint sequence, long granule, byte headerType)
        {
            Packets.Add(packet);
            SerialNumber = serialNumber;
            PageSequenceNumber = sequence;
            GranulePosition = granule;
            HeaderType = headerType;
        }

        public byte[] ToByteArray()
        {
            var segmentTable = new List<byte>();
            var totalPacketSize = 0;
            foreach (var packet in Packets)
            {
                totalPacketSize += packet.Length;
                var numSegments = packet.Length / 255;
                for (var i = 0; i < numSegments; i++) segmentTable.Add(255);
                segmentTable.Add((byte)(packet.Length % 255));
            }

            var pageHeaderSize = 27 + segmentTable.Count;
            var pageBytes = new byte[pageHeaderSize + totalPacketSize];

            pageBytes[0] = (byte)'O';
            pageBytes[1] = (byte)'g';
            pageBytes[2] = (byte)'g';
            pageBytes[3] = (byte)'S';
            pageBytes[4] = Version;
            pageBytes[5] = HeaderType;
            Buffer.BlockCopy(BitConverter.GetBytes(GranulePosition), 0, pageBytes, 6, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(SerialNumber), 0, pageBytes, 14, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(PageSequenceNumber), 0, pageBytes, 18, 4);
            pageBytes[26] = (byte)segmentTable.Count;
            Buffer.BlockCopy(segmentTable.ToArray(), 0, pageBytes, 27, segmentTable.Count);

            int currentPos = pageHeaderSize;
            foreach (var packet in Packets)
            {
                Buffer.BlockCopy(packet, 0, pageBytes, currentPos, packet.Length);
                currentPos += packet.Length;
            }

            var crc = OggCrc.Calculate(pageBytes, 0, pageBytes.Length);
            Buffer.BlockCopy(BitConverter.GetBytes(crc), 0, pageBytes, 22, 4);

            return pageBytes;
        }

        public static async Task<Result<OggPage?>> FromStreamAsync(Stream stream)
        {
            try
            {
                int b2 = -1, b3 = -1, b4 = -1;
                while (stream.Position < stream.Length)
                {
                    var b1 = b2;
                    b2 = b3;
                    b3 = b4;
                    b4 = stream.ReadByte();
                    if (b1 == 'O' && b2 == 'g' && b3 == 'g' && b4 == 'S')
                    {
                        break;
                    }
                }

                if (b4 == -1) return Result<OggPage?>.Ok(null);

                var headerBuffer = new byte[23];
                await stream.ReadExactlyAsync(headerBuffer);

                var page = new OggPage
                {
                    Version = headerBuffer[0],
                    HeaderType = headerBuffer[1],
                    GranulePosition = BitConverter.ToInt64(headerBuffer, 2),
                    SerialNumber = BitConverter.ToUInt32(headerBuffer, 10),
                    PageSequenceNumber = BitConverter.ToUInt32(headerBuffer, 14)
                };

                var pageSegments = headerBuffer[22];
                var segmentTable = new byte[pageSegments];
                await stream.ReadExactlyAsync(segmentTable);

                var currentPacketBuffer = new MemoryStream();
                foreach (var segmentLength in segmentTable)
                {
                    var segmentData = new byte[segmentLength];
                    await stream.ReadExactlyAsync(segmentData);
                    currentPacketBuffer.Write(segmentData, 0, segmentData.Length);
                    if (segmentLength < 255)
                    {
                        page.Packets.Add(currentPacketBuffer.ToArray());
                        currentPacketBuffer = new MemoryStream();
                    }
                }
                
                if (currentPacketBuffer.Length > 0) page.Packets.Add(currentPacketBuffer.ToArray());

                return page;
            }
            catch (EndOfStreamException ex)
            {
                return new CorruptChunkError("Ogg Page", "Page is truncated or a segment is incorrect.", ex);
            }
        }
    }
}