using System.Security.Cryptography;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;

namespace SoundFlow.Security;

/// <summary>
/// Provides methods to sign and verify files using ECDSA Digital Signatures.
/// This ensures file authenticity and integrity at the binary container level.
/// </summary>
public static class FileAuthenticator
{
    private const int BufferSize = 8192;

    /// <summary>
    /// Asynchronously calculates a digital signature for a specific file.
    /// </summary>
    /// <param name="filePath">The path to the file to sign.</param>
    /// <param name="config">The configuration containing the Private Key in PEM format.</param>
    /// <returns>A result containing the Base64-encoded signature string.</returns>
    public static async Task<Result<string>> SignFileAsync(string filePath, SignatureConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.PrivateKeyPem))
            return new ValidationError("Private Key is required for signing.");

        if (!File.Exists(filePath))
            return new NotFoundError("File", $"File to sign not found: {filePath}");

        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            return await SignStreamAsync(fileStream, config);
        }
        catch (IOException ex)
        {
            return new IoError($"reading file '{filePath}' for signing", ex);
        }
    }

    /// <summary>
    /// Asynchronously calculates a digital signature for a data stream.
    /// </summary>
    /// <param name="stream">The stream to sign. Must be readable.</param>
    /// <param name="config">The configuration containing the Private Key in PEM format.</param>
    /// <returns>A result containing the Base64-encoded signature string.</returns>
    public static async Task<Result<string>> SignStreamAsync(Stream stream, SignatureConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.PrivateKeyPem))
            return new ValidationError("Private Key is required for signing.");

        try
        {
            // Asynchronously compute the hash of the stream.
            using var hashAlgorithm = SHA384.Create();
            var dataHash = await hashAlgorithm.ComputeHashAsync(stream);

            // Sign the computed hash.
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(config.PrivateKeyPem);
            var signatureBytes = ecdsa.SignHash(dataHash);
            
            return Convert.ToBase64String(signatureBytes);
        }
        catch (CryptographicException ex)
        {
            return new ValidationError("Invalid Private Key format or cryptographic error during signing.", ex);
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred during signing.", ex);
        }
    }

    /// <summary>
    /// Asynchronously verifies the authenticity of a file against a provided signature.
    /// </summary>
    /// <param name="filePath">The path to the file to verify.</param>
    /// <param name="signatureBase64">The Base64-encoded signature to verify against.</param>
    /// <param name="config">The configuration containing the Public Key in PEM format.</param>
    /// <returns>A result containing true if the signature is valid, otherwise false.</returns>
    public static async Task<Result<bool>> VerifyFileAsync(string filePath, string signatureBase64, SignatureConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.PublicKeyPem))
            return new ValidationError("Public Key is required for verification.");

        if (!File.Exists(filePath))
            return new NotFoundError("File", $"File to verify not found: {filePath}");

        try
        {
            await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            return await VerifyStreamAsync(fileStream, signatureBase64, config);
        }
        catch (IOException ex)
        {
            return new IoError($"reading file '{filePath}' for verification", ex);
        }
    }

    /// <summary>
    /// Asynchronously verifies the authenticity of a data stream against a provided signature.
    /// </summary>
    /// <param name="stream">The stream to verify.</param>
    /// <param name="signatureBase64">The Base64-encoded signature to verify against.</param>
    /// <param name="config">The configuration containing the Public Key in PEM format.</param>
    /// <returns>A result containing true if the signature is valid, otherwise false.</returns>
    public static async Task<Result<bool>> VerifyStreamAsync(Stream stream, string signatureBase64, SignatureConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.PublicKeyPem))
            return new ValidationError("Public Key is required for verification.");

        if (string.IsNullOrWhiteSpace(signatureBase64))
            return new ValidationError("Signature cannot be empty for verification.");

        try
        {
            var signatureBytes = Convert.FromBase64String(signatureBase64);

            // Asynchronously compute the hash of the stream.
            using var hashAlgorithm = SHA384.Create();
            var dataHash = await hashAlgorithm.ComputeHashAsync(stream);

            // Verify the computed hash against the signature.
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(config.PublicKeyPem);
            var isValid = ecdsa.VerifyHash(dataHash, signatureBytes);

            return isValid;
        }
        catch (Exception ex)
        {
            return ex switch
            {
                FormatException formatEx => new ValidationError("The provided signature is not a valid Base64 string.", formatEx),
                CryptographicException cryptEx => new ValidationError("Invalid Public Key format or cryptographic error during verification.", cryptEx),
                _ => new Error("An unexpected error occurred during verification.", ex)
            };
        }
    }
}