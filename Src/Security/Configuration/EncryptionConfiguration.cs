namespace SoundFlow.Security.Configuration;

/// <summary>
/// Configuration settings for audio stream encryption.
/// </summary>
public class EncryptionConfiguration
{
    /// <summary>
    /// Gets or sets the encryption key. Must be 32 bytes (256 bits) for AES-256.
    /// </summary>
    public byte[] Key { get; set; } = [];

    /// <summary>
    /// Gets or sets the initialization vector (Nonce).
    /// For AES-CTR, this is typically 12 bytes, with the last 4 bytes reserved for the counter.
    /// </summary>
    public byte[] Iv { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether to compute an HMAC integrity tag during processing.
    /// </summary>
    public bool EnableIntegrityCheck { get; set; } = true;
}