using System.Text;
using SoundFlow.Enums;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;

namespace SoundFlow.Security.Containers;

/// <summary>
/// A utility class for handling the metadata headers of encrypted audio containers.
/// </summary>
public static class SecureAudioContainer
{
    private static readonly byte[] MagicHeader = "SFA_ENC"u8.ToArray();
    private const int HeaderVersion = 1;
    
    /// <summary>
    /// Fixed size reserved for the signature block (Length + Data + Padding) if embedded.
    /// 512 bytes is sufficient for ECDSA P-384 (approx 100-120 bytes) and future proofing.
    /// </summary>
    public const int MaxSignatureSize = 512;

    /// <summary>
    /// Container flags to indicate the presence of an embedded digital signature.
    /// </summary>
    [Flags]
    public enum ContainerFlags : uint
    {
        /// <summary>
        /// No flags set.
        /// </summary>
        None = 0,
        /// <summary>
        /// The container has an embedded digital signature.
        /// </summary>
        HasEmbeddedSignature = 1 << 0
    }

    /// <summary>
    /// Writes the Secure Audio container header to the stream.
    /// </summary>
    /// <param name="outputStream">The destination stream.</param>
    /// <param name="config">The encryption configuration containing the IV.</param>
    /// <param name="originalFormat">The format of the audio data.</param>
    /// <param name="embedSignature">Whether to reserve space for an embedded digital signature.</param>
    /// <returns>The byte offset where the signature block begins (if embedded), or -1.</returns>
    public static long WriteHeader(Stream outputStream, EncryptionConfiguration config, AudioFormat originalFormat, bool embedSignature = false)
    {
        using var writer = new BinaryWriter(outputStream, Encoding.UTF8, true);

        // Write Header Magic
        writer.Write(MagicHeader);

        // Write Version
        writer.Write(HeaderVersion);
        
        // Write Flags
        var flags = embedSignature ? ContainerFlags.HasEmbeddedSignature : ContainerFlags.None;
        writer.Write((uint)flags);

        // Write Format Metadata
        writer.Write(originalFormat.SampleRate);
        writer.Write(originalFormat.Channels);
        writer.Write((int)originalFormat.Format);

        // Write Encryption Metadata
        writer.Write(config.Iv.Length);
        writer.Write(config.Iv);
        
        // Write Signature Placeholder (if requested)
        long sigOffset = -1;
        if (embedSignature)
        {
            writer.Flush();
            sigOffset = outputStream.Position;
            // Write zeroed placeholder
            writer.Write(new byte[MaxSignatureSize]);
        }
        
        return sigOffset;
    }

    /// <summary>
    /// Reads the Secure Audio container header from the stream.
    /// </summary>
    /// <param name="inputStream">The source stream.</param>
    /// <returns>
    /// A result containing the audio format, the IV, the data start offset, 
    /// and optionally the extracted signature bytes and its file offset if present.
    /// </returns>
    public static Result<(AudioFormat Format, byte[] IV, long DataStartOffset, byte[]? Signature, long SigBlockOffset)> ReadHeader(Stream inputStream)
    {
        try
        {
            using var reader = new BinaryReader(inputStream, Encoding.UTF8, true);

            // Verify Magic
            var magic = reader.ReadBytes(MagicHeader.Length);
            if (!magic.SequenceEqual(MagicHeader))
                return new HeaderNotFoundError("Secure Audio Container Magic");

            // Check Version
            var version = reader.ReadInt32();
            if (version != HeaderVersion)
                return new UnsupportedFormatError($"Unknown Container Version: {version}");
            
            // Read Flags
            var flags = (ContainerFlags)reader.ReadUInt32();

            // Read Format
            var sampleRate = reader.ReadInt32();
            var channels = reader.ReadInt32();
            var format = (SampleFormat)reader.ReadInt32();

            var audioFormat = new AudioFormat
            {
                SampleRate = sampleRate,
                Channels = channels,
                Format = format,
                Layout = AudioFormat.GetLayoutFromChannels(channels)
            };

            // Read Encryption Metadata
            var ivLength = reader.ReadInt32();
            var iv = reader.ReadBytes(ivLength);
            
            // Read Signature (if present)
            byte[]? signature = null;
            long sigBlockOffset = -1;

            if (flags.HasFlag(ContainerFlags.HasEmbeddedSignature))
            {
                sigBlockOffset = inputStream.Position;
                var sigBytesWithHeader = reader.ReadBytes(MaxSignatureSize);
                
                if (sigBytesWithHeader.Length < 4)
                    return new CorruptChunkError("SignatureBlock", "Truncated signature block.");

                // Parse inner length
                var sigLen = BitConverter.ToInt32(sigBytesWithHeader, 0);
                if (sigLen is > 0 and <= MaxSignatureSize - 4)
                {
                    signature = new byte[sigLen];
                    Array.Copy(sigBytesWithHeader, 4, signature, 0, sigLen);
                }
            }

            return (audioFormat, iv, inputStream.Position, signature, sigBlockOffset);
        }
        catch (Exception ex)
        {
            return new IoError("Failed to read container header.", ex);
        }
    }
}