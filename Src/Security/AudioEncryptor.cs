using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Models;
using SoundFlow.Providers;
using SoundFlow.Security.Configuration;
using SoundFlow.Security.Containers;
using SoundFlow.Security.Modifiers;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Security;

/// <summary>
/// Provides high-level methods to encrypt and decrypt audio using the <see cref="SecureAudioContainer"/> format.
/// </summary>
public static class AudioEncryptor
{
    private const int BufferSize = 8192; // 8KB buffer

    /// <summary>
    /// Asynchronously reads audio from the source provider, encrypts it, and writes it to the destination stream.
    /// Uses streaming to support large files without high memory usage.
    /// Optionally signs the destination stream. If <paramref name="embedSignature"/> is true, the signature is embedded in the header; otherwise, it is returned.
    /// </summary>
    /// <param name="source">The source audio provider.</param>
    /// <param name="destinationStream">The output stream. If signing is requested, it must be readable and seekable.</param>
    /// <param name="config">The encryption configuration.</param>
    /// <param name="signingConfig">Configuration to sign the data. If null, no signing occurs.</param>
    /// <param name="embedSignature">If true and <paramref name="signingConfig"/> is present, the signature is embedded into the container header.</param>
    /// <returns>
    /// A task containing the Base64 signature string if signing was performed and <paramref name="embedSignature"/> was false.
    /// Returns null if no signing occurred or if the signature was embedded.
    /// </returns>
    public static async Task<string?> EncryptAsync(
        ISoundDataProvider source, 
        Stream destinationStream, 
        EncryptionConfiguration config, 
        SignatureConfiguration? signingConfig = null,
        bool embedSignature = false)
    {
        var format = new AudioFormat
        {
            SampleRate = source.SampleRate,
            Channels = source.FormatInfo?.ChannelCount ?? 2,
            Format = source.SampleFormat,
            Layout = AudioFormat.GetLayoutFromChannels(source.FormatInfo?.ChannelCount ?? 2)
        };

        var shouldSign = signingConfig != null;
        if (shouldSign && (!destinationStream.CanSeek || !destinationStream.CanRead))
        {
            Log.Warning("Skipping signature generation: Destination stream is not seekable or readable.");
            shouldSign = false;
        }

        // 1. Write Header (with placeholder if embedding)
        var sigBlockOffset = SecureAudioContainer.WriteHeader(destinationStream, config, format, embedSignature && shouldSign);

        // 2. Prepare source
        if (source.CanSeek) source.Seek(0);
        using var modifier = new StreamEncryptionModifier(config) { Enabled = true };
        
        var sampleBuffer = ArrayPool<float>.Shared.Rent(BufferSize);
        var byteBuffer = ArrayPool<byte>.Shared.Rent(BufferSize * sizeof(float));

        try
        {
            while (true)
            {
                // Process the next block of audio synchronously
                var bytesAvailable = ProcessNextBlock(source, modifier, sampleBuffer, byteBuffer, format.Channels);
                if (bytesAvailable == 0) break;
                
                // Write the encrypted block to the destination stream asynchronously
                await destinationStream.WriteAsync(byteBuffer.AsMemory(0, bytesAvailable));
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(sampleBuffer);
            ArrayPool<byte>.Shared.Return(byteBuffer);
        }

        // 3. Perform Signing if requested
        if (shouldSign)
        {
            await destinationStream.FlushAsync();
            destinationStream.Seek(0, SeekOrigin.Begin); // Rewind for full stream hashing

            // If embedding, the current file state has zeros in the signature block.
            // This is exactly what we want to hash.
            var sigResult = await FileAuthenticator.SignStreamAsync(destinationStream, signingConfig!);

            if (sigResult.IsFailure)
            {
                Log.Error($"Failed to generate signature: {sigResult.Error?.Message}");
                return null;
            }

            var signatureBase64 = sigResult.Value;

            if (embedSignature && sigBlockOffset >= 0 && signatureBase64 != null)
            {
                // Patch the header with the signature
                var sigBytes = Convert.FromBase64String(signatureBase64);
                if (sigBytes.Length + 4 > SecureAudioContainer.MaxSignatureSize)
                {
                    Log.Error("Generated signature is too large for the reserved container space.");
                    return null;
                }

                destinationStream.Seek(sigBlockOffset, SeekOrigin.Begin);
                await using var writer = new BinaryWriter(destinationStream, Encoding.UTF8, true);
                writer.Write(sigBytes.Length);
                writer.Write(sigBytes);
                writer.Flush();
                
                // Return to end of stream
                destinationStream.Seek(0, SeekOrigin.End);
                return null; // Signature is embedded
            }
            
            // Return detached signature
            destinationStream.Seek(0, SeekOrigin.End);
            return signatureBase64;
        }
        
        return null;

        // Read bytes from the source provider and encrypt them in-place, method isolated to allow using spans in async context
        static int ProcessNextBlock(ISoundDataProvider provider, StreamEncryptionModifier encryptionModifier, float[] floatArr, byte[] byteArr, int channelCount)
        {
            // Create Spans here. Since this method is not async, this is perfectly valid.
            var spanFloat = floatArr.AsSpan();
            
            var samplesRead = provider.ReadBytes(spanFloat);
            if (samplesRead == 0) return 0;

            var validSlice = spanFloat[..samplesRead];
            
            // Encrypt in-place
            encryptionModifier.Process(validSlice, channelCount);

            // Copy to byte buffer
            var bytesToWrite = samplesRead * sizeof(float);
            Buffer.BlockCopy(floatArr, 0, byteArr, 0, bytesToWrite);

            return bytesToWrite;
        }
    }

    /// <summary>
    /// Reads audio from the source provider, encrypts it, and writes it to the destination file path.
    /// Optionally signs the result (either embedded or detached).
    /// </summary>
    public static async Task EncryptAsync(
        ISoundDataProvider source, 
        string destinationPath, 
        EncryptionConfiguration config, 
        SignatureConfiguration? signingConfig = null,
        bool embedSignature = false)
    {
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var detachedSignature = await EncryptAsync(source, fileStream, config, signingConfig, embedSignature);
        if (detachedSignature != null)
        {
            await File.WriteAllTextAsync(destinationPath + ".sig", detachedSignature);
        }
    }

    /// <summary>
    /// Reads an encrypted stream and returns a provider that streams the decrypted audio.
    /// The source stream remains open and its ownership is transferred to the returned provider,
    /// which will dispose of the stream when it is disposed.
    /// </summary>
    /// <param name="sourceStream">The stream containing the secure audio data.</param>
    /// <param name="key">The decryption key.</param>
    /// <returns>A result containing a streaming provider, or an error.</returns>
    public static Result<ISoundDataProvider> Decrypt(Stream sourceStream, byte[] key)
    {
        try
        {
            var headerResult = SecureAudioContainer.ReadHeader(sourceStream);

            if (headerResult.IsFailure)
            {
                sourceStream.Dispose();
                return Result<ISoundDataProvider>.Fail(headerResult.Error!);
            }

            var (format, iv, dataOffset, embeddedSigBytes, _) = headerResult.Value;

            if (embeddedSigBytes != null)
            {
                Log.Warning($"This file contains a digital signature that is being ignored. Use {nameof(VerifyAndDecryptAsync)} for authenticated decryption.");
            }

            var config = new EncryptionConfiguration { Key = key, Iv = iv };

            // The DecryptionStream takes ownership of the sourceStream.
            var cryptoStream = new DecryptionStream(sourceStream, config, dataOffset);

            // Wrap in RawDataProvider as the stream produces raw PCM data.
            var provider = new RawDataProvider(cryptoStream, format.Format, format.SampleRate)
            {
                FormatInfo = new SoundFormatInfo
                {
                    FormatName = "Decrypted Audio",
                    FormatIdentifier = "raw",
                    SampleRate = format.SampleRate,
                    ChannelCount = format.Channels,
                    IsLossless = true
                }
            };
            
            return Result<ISoundDataProvider>.Ok(provider);
        }
        catch (Exception ex)
        {
            // Ensure the stream is disposed on any failure during initialization.
            sourceStream.Dispose();
            return ex switch
            {
                ObjectDisposedException => new ObjectDisposedError("sourceStream"),
                CryptographicException cryptoEx =>
                    new ValidationError("Invalid cryptographic configuration. The provided key may have an incorrect size.", cryptoEx),
                ArgumentException argEx =>
                    new ValidationError($"Invalid argument during decryption initialization: {argEx.Message}", argEx),
                IOException ioEx =>
                    new IoError("Initializing decryption stream", ioEx),
                _ => new InvalidOperationError("An unexpected error occurred while initializing the decryption provider.", ex)
            };
        }
    }

    /// <summary>
    /// Opens an encrypted file and returns a provider that streams the decrypted audio.
    /// The file remains locked until the provider is disposed.
    /// </summary>
    /// <param name="sourceFilePath">The path to the secure audio file.</param>
    /// <param name="key">The decryption key.</param>
    /// <returns>A result containing a streaming provider, or an error.</returns>
    public static Result<ISoundDataProvider> Decrypt(string sourceFilePath, byte[] key)
    {
        if (!File.Exists(sourceFilePath)) 
            return new NotFoundError("File", $"The specified file was not found: '{sourceFilePath}'.");

        try
        {
            var fileStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read);
            return Decrypt(fileStream, key);
        }
        catch (Exception ex)
        {
            return ex switch
            {
                UnauthorizedAccessException =>
                    new AccessDeniedError(sourceFilePath),
                DirectoryNotFoundException =>
                    new NotFoundError("Directory", $"The directory for the specified path was not found: '{sourceFilePath}'."),
                PathTooLongException or ArgumentException =>
                    new ValidationError($"The file path is invalid: '{sourceFilePath}'.", ex),
                NotSupportedException nsEx =>
                    new ValidationError($"The file path format is not supported: '{sourceFilePath}'.", nsEx),
                IOException ioEx =>
                    new IoError($"opening the file '{sourceFilePath}'", ioEx),
                _ => new HostError($"An unexpected OS error occurred when opening '{sourceFilePath}'.", ex)
            };
        }
    }

    /// <summary>
    /// Opens an encrypted file, verifies its authenticity (embedded or detached), and returns a decryption provider.
    /// </summary>
    /// <param name="sourceFilePath">The path to the secure audio file.</param>
    /// <param name="key">The decryption key.</param>
    /// <param name="signingConfig">The configuration containing the Public Key.</param>
    /// <param name="detachedSignature">Optional detached signature. If null, the method looks for an embedded signature.</param>
    /// <returns>A result containing the decrypted provider if verification succeeds.</returns>
    public static async Task<Result<ISoundDataProvider>> VerifyAndDecryptAsync(
        string sourceFilePath, 
        byte[] key, 
        SignatureConfiguration signingConfig, 
        string? detachedSignature = null)
    {
        if (!File.Exists(sourceFilePath))
            return new NotFoundError("File", $"File not found: {sourceFilePath}");

        try
        {
            var fileStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var result = await VerifyAndDecryptAsync(fileStream, key, signingConfig, detachedSignature);
            
            if (result.IsFailure) 
                await fileStream.DisposeAsync();

            return result;
        }
        catch (Exception ex)
        {
            return new IoError($"opening file '{sourceFilePath}'", ex);
        }
    }

    /// <summary>
    /// Verifies the authenticity of an encrypted stream and returns a decryption provider.
    /// Handles both embedded signatures (by masking the signature block with zeros) and detached signatures.
    /// </summary>
    public static async Task<Result<ISoundDataProvider>> VerifyAndDecryptAsync(
        Stream sourceStream, 
        byte[] key, 
        SignatureConfiguration signingConfig, 
        string? detachedSignature = null)
    {
        if (!sourceStream.CanSeek)
            return new InvalidOperationError("Stream must be seekable for verification.");

        // 1. Read Header to find embedded signature info
        var headerStart = sourceStream.Position;
        var headerResult = SecureAudioContainer.ReadHeader(sourceStream);
        
        if (headerResult.IsFailure)
            return Result<ISoundDataProvider>.Fail(headerResult.Error!);

        var (format, iv, dataOffset, embeddedSigBytes, sigOffset) = headerResult.Value;
        
        // 2. Determine which signature to use
        var signatureToVerify = detachedSignature;
        var isEmbedded = false;

        if (string.IsNullOrEmpty(detachedSignature) && embeddedSigBytes != null)
        {
            signatureToVerify = Convert.ToBase64String(embeddedSigBytes);
            isEmbedded = true;
        }

        if (string.IsNullOrEmpty(signatureToVerify))
            return new ValidationError("No signature found (neither embedded nor detached provided).");

        // 3. Verification
        sourceStream.Seek(headerStart, SeekOrigin.Begin);
        Result<bool> verifyResult;

        if (isEmbedded && sigOffset >= 0)
        {
            // We must verify using a stream that "sees" zeros where the signature currently is.
            await using var zeroingStream = new ZeroingStream(sourceStream, sigOffset, SecureAudioContainer.MaxSignatureSize);
            verifyResult = await FileAuthenticator.VerifyStreamAsync(zeroingStream, signatureToVerify, signingConfig);
        }
        else
        {
            // Standard verification
            verifyResult = await FileAuthenticator.VerifyStreamAsync(sourceStream, signatureToVerify, signingConfig);
        }

        if (verifyResult.IsFailure) 
            return Result<ISoundDataProvider>.Fail(verifyResult.Error!);
        
        if (!verifyResult.Value)
            return new ValidationError("Integrity check failed. Signature mismatch.");

        // 4. Setup Decryption
        // Reset stream to data start
        sourceStream.Seek(dataOffset, SeekOrigin.Begin);
        
        var encryptionConfig = new EncryptionConfiguration { Key = key, Iv = iv };
        var cryptoStream = new DecryptionStream(sourceStream, encryptionConfig, dataOffset);

        var provider = new RawDataProvider(cryptoStream, format.Format, format.SampleRate)
        {
            FormatInfo = new SoundFormatInfo
            {
                FormatName = "Decrypted Audio",
                FormatIdentifier = "raw",
                SampleRate = format.SampleRate,
                ChannelCount = format.Channels,
                IsLossless = true
            }
        };

        return Result<ISoundDataProvider>.Ok(provider);
    }

    /// <summary>
    /// A stream wrapper that zeroes out a specific range of bytes during read.
    /// Used to simulate the "pre-signed" state of the file header during verification.
    /// </summary>
    private sealed class ZeroingStream(Stream baseStream, long zeroStart, int zeroLength) : Stream
    {
        private readonly long _zeroEnd = zeroStart + zeroLength;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = baseStream.Read(buffer, offset, count);
            if (bytesRead == 0) return 0;

            var currentPos = baseStream.Position - bytesRead;
            var endPos = baseStream.Position;

            // Check intersection with zero region
            if (endPos > zeroStart && currentPos < _zeroEnd)
            {
                var overlapStart = Math.Max(currentPos, zeroStart);
                var overlapEnd = Math.Min(endPos, _zeroEnd);
                var lengthToZero = (int)(overlapEnd - overlapStart);
                var bufferOffset = offset + (int)(overlapStart - currentPos);

                Array.Clear(buffer, bufferOffset, lengthToZero);
            }

            return bytesRead;
        }

        public override int Read(Span<byte> buffer)
        {
            var bytesRead = baseStream.Read(buffer);
            if (bytesRead == 0) return 0;

            var currentPos = baseStream.Position - bytesRead;
            var endPos = baseStream.Position;

            if (endPos > zeroStart && currentPos < _zeroEnd)
            {
                var overlapStart = Math.Max(currentPos, zeroStart);
                var overlapEnd = Math.Min(endPos, _zeroEnd);
                var lengthToZero = (int)(overlapEnd - overlapStart);
                var bufferOffset = (int)(overlapStart - currentPos);

                buffer.Slice(bufferOffset, lengthToZero).Clear();
            }

            return bytesRead;
        }

        // Passthrough members
        public override bool CanRead => baseStream.CanRead;
        public override bool CanSeek => baseStream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => baseStream.Length;
        public override long Position { get => baseStream.Position; set => baseStream.Position = value; }
        public override void Flush() => baseStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => baseStream.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Internal stream wrapper that decrypts data on-the-fly during Read operations.
    /// Supports seeking by recalculating AES-CTR state.
    /// </summary>
    private sealed class DecryptionStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly StreamEncryptionModifier _modifier;
        private readonly long _dataOffset;

        public DecryptionStream(Stream baseStream, EncryptionConfiguration config, long dataOffset)
        {
            _baseStream = baseStream;
            _dataOffset = dataOffset;
            _modifier = new StreamEncryptionModifier(config) { Enabled = true };

            // Ensure stream is positioned at the start of the audio data.
            if (_baseStream.Position != _dataOffset)
                _baseStream.Seek(_dataOffset, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _baseStream.Read(buffer, offset, count);
            if (read > 0)
            {
                // Decrypt the data that was just read, in-place.
                _modifier.ProcessBytes(buffer.AsSpan(offset, read));
            }

            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _baseStream.Read(buffer);
            if (read > 0)
            {
                _modifier.ProcessBytes(buffer[..read]);
            }

            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            // Calculate the absolute target position in the base stream.
            var targetPos = origin switch
            {
                SeekOrigin.Begin => _dataOffset + offset,
                SeekOrigin.Current => _baseStream.Position + offset,
                SeekOrigin.End => _baseStream.Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            // Clamp the position to the valid range of audio data.
            if (targetPos < _dataOffset) targetPos = _dataOffset;
            if (targetPos > _baseStream.Length) targetPos = _baseStream.Length;

            // Perform the file seek
            _baseStream.Seek(targetPos, SeekOrigin.Begin);

            // Determine the relative offset from the start of the audio data.
            var relativeOffset = targetPos - _dataOffset;

            // Re-synchronize the AES-CTR modifier to this exact byte offset.
            _modifier.SeekTo(relativeOffset);

            return relativeOffset;
        }

        public override void SetLength(long value) => throw new NotSupportedException("The stream is read-only.");

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("The stream is read-only.");

        public override void Flush() => _baseStream.Flush();
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _baseStream.Length - _dataOffset;

        public override long Position
        {
            get => _baseStream.Position - _dataOffset;
            set => Seek(value, SeekOrigin.Begin);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _modifier.Dispose();
                _baseStream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}