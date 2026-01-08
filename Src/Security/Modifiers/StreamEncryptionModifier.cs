using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using SoundFlow.Abstracts;
using SoundFlow.Security.Configuration;
using Aes = System.Security.Cryptography.Aes;

namespace SoundFlow.Security.Modifiers;

/// <summary>
/// A modifier that performs real-time AES-256-CTR encryption or decryption.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses AES-CTR (Counter Mode).
/// CTR mode is used because it transforms the block cipher into a stream cipher.
/// This preserves the length of the data exactly (no padding), which is critical
/// for maintaining the sample-count synchronization of the audio engine.
/// </para>
/// </remarks>
public sealed class StreamEncryptionModifier : SoundModifier, IDisposable
{
    private readonly Aes _aes;
    private readonly ICryptoTransform _ecbEncryptor;
    
    private readonly byte[] _counterBlock;
    private readonly byte[] _keyStreamBlock;
    private readonly uint _initialCounter;
    private int _keyStreamIndex;

    /// <inheritdoc />
    public override string Name { get; set; } = "AES-256-CTR Encryption";

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamEncryptionModifier"/> class.
    /// </summary>
    /// <param name="config">The encryption configuration containing the Key (32 bytes) and IV (12 or 16 bytes).</param>
    public StreamEncryptionModifier(EncryptionConfiguration config)
    {
        if (config.Key.Length != 32)
            throw new ArgumentException("AES-256 requires a 32-byte key.", nameof(config));
        
        if (config.Iv.Length != 12 && config.Iv.Length != 16)
            throw new ArgumentException("AES-CTR recommends a 12-byte nonce or 16-byte IV.", nameof(config));

        _aes = Aes.Create();
        _aes.KeySize = 256;
        _aes.Key = config.Key;
        _aes.Mode = CipherMode.ECB; // CTR mode is implemented by encrypting a counter with ECB.
        _aes.Padding = PaddingMode.None;

        _ecbEncryptor = _aes.CreateEncryptor();

        _counterBlock = new byte[16];
        _keyStreamBlock = new byte[16];
        
        var ivLength = Math.Min(16, config.Iv.Length);
        Buffer.BlockCopy(config.Iv, 0, _counterBlock, 0, ivLength);

        // Capture initial counter state (last 4 bytes, Big Endian) for seeking logic
        // If IV is 12 bytes, the counter part is 0. If 16 bytes, it's the last 4 bytes.
        _initialCounter = ivLength >= 16 ? BinaryPrimitives.ReadUInt32BigEndian(_counterBlock.AsSpan(12, 4)) : 0;

        // Set index to 16 to force generation of a new keystream block on the first Process call.
        _keyStreamIndex = 16;
    }

    /// <summary>
    /// Resets the internal cryptographic state to a specific byte offset in the stream.
    /// This allows for random access seeking within the encrypted stream.
    /// </summary>
    /// <param name="byteOffset">The absolute byte offset from the beginning of the data.</param>
    public void SeekTo(long byteOffset)
    {
        // 1. Calculate which 16-byte block we are in.
        var blockIndex = byteOffset / 16;
        
        // 2. Calculate the offset within that block.
        _keyStreamIndex = (int)(byteOffset % 16);

        // 3. Calculate the new counter value (Counter = InitialCounter + BlockIndex)
        var newCounter = unchecked(_initialCounter + (uint)blockIndex);

        // 4. Update the counter block (Bytes 12-15)
        BinaryPrimitives.WriteUInt32BigEndian(_counterBlock.AsSpan(12, 4), newCounter);

        // 5. If we are not aligned perfectly to a block start, we need to pre-generate 
        GenerateKeyStreamBlock(false);
    }

    /// <inheritdoc />
    public override void Process(Span<float> buffer, int channels)
    {
        if (!Enabled) return;

        // Treat the float buffer as a raw byte stream for cryptographic operations.
        var byteBuffer = MemoryMarshal.Cast<float, byte>(buffer);
        ProcessBytes(byteBuffer);
    }

    /// <inheritdoc />
    public override float ProcessSample(float sample, int channel) => 
        throw new NotSupportedException("Use the block-based Process method for high-performance encryption.");

    /// <summary>
    /// Processes the byte buffer using SIMD instructions where possible.
    /// </summary>
    /// <param name="data">The data to encrypt/decrypt.</param>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public unsafe void ProcessBytes(Span<byte> data)
    {
        fixed (byte* pData = data)
        fixed (byte* pKeyStream = _keyStreamBlock)
        {
            var i = 0;
            var length = data.Length;

            while (i < length)
            {
                // 1. If we exhausted the current keystream block, generate the next one.
                if (_keyStreamIndex >= 16)
                {
                    // Encrypt the current counter, then increment for the next round
                    GenerateKeyStreamBlock(incrementCounter: true);
                    _keyStreamIndex = 0;
                }

                // Determine number of bytes to process, limited by remaining data and current keystream.
                var remainingInKeystream = 16 - _keyStreamIndex;
                var remainingInData = length - i;
                var bytesToProcess = Math.Min(remainingInKeystream, remainingInData);

                // Use SIMD for full 16-byte blocks.
                if (Vector128.IsHardwareAccelerated && bytesToProcess == 16)
                {
                    var vData = Sse2.LoadVector128(pData + i);
                    var vKey = Sse2.LoadVector128(pKeyStream);
                    var vResult = Sse2.Xor(vData, vKey);
                    Sse2.Store(pData + i, vResult);
                    
                    i += 16;
                    _keyStreamIndex += 16;
                }
                else
                {
                    // Fallback for partial blocks or non-SIMD hardware.
                    for (var j = 0; j < bytesToProcess; j++)
                    {
                        pData[i] ^= pKeyStream[_keyStreamIndex];
                        i++;
                        _keyStreamIndex++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Generates the next 16 bytes of the keystream by encrypting the current counter value.
    /// Increments the counter afterwards.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GenerateKeyStreamBlock(bool incrementCounter)
    {
        // Encrypt counter to get keystream
        _ecbEncryptor.TransformBlock(_counterBlock, 0, 16, _keyStreamBlock, 0);
        
        if (incrementCounter)
        {
            IncrementCounterBigEndian();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void IncrementCounterBigEndian()
    {
        // Read the last 4 bytes as a UInt32 in Big Endian
        var counterSpan = _counterBlock.AsSpan(12, 4);
        var counterValue = BinaryPrimitives.ReadUInt32BigEndian(counterSpan);

        // Standard wrapping overflow is the correct behavior for CTR mode.
        unchecked
        {
            counterValue++;
        }

        // Write back
        BinaryPrimitives.WriteUInt32BigEndian(counterSpan, counterValue);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _aes.Dispose();
        _ecbEncryptor.Dispose();
    }
}