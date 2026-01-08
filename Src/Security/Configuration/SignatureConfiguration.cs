using SoundFlow.Security.Utils;

namespace SoundFlow.Security.Configuration;

/// <summary>
/// Configuration settings for digital signature operations (signing and verification).
/// </summary>
public class SignatureConfiguration
{
    /// <summary>
    /// Gets or sets the PEM-encoded Private Key (PKCS#8).
    /// Required for <see cref="FileAuthenticator.SignStreamAsync(Stream, SignatureConfiguration)"/>.
    /// <remarks>Never distribute this key with your application.</remarks>
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>
    /// Gets or sets the PEM-encoded Public Key (SubjectPublicKeyInfo).
    /// Required for <see cref="FileAuthenticator.VerifyStreamAsync(Stream, string, SignatureConfiguration)"/>.
    /// <remarks>This key is safe to distribute.</remarks>
    /// </summary>
    public string? PublicKeyPem { get; set; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="SignatureConfiguration"/> class.
    /// </summary>
    /// <param name="privateKeyPem">The PEM-encoded Private Key (PKCS#8).</param>
    /// <param name="publicKeyPem">The PEM-encoded Public Key (SubjectPublicKeyInfo).</param>
    public SignatureConfiguration(string privateKeyPem, string publicKeyPem)
    {
        PrivateKeyPem = privateKeyPem;
        PublicKeyPem = publicKeyPem;
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="SignatureConfiguration"/> class.
    /// </summary>
    public SignatureConfiguration()
    {
        PrivateKeyPem = null;
        PublicKeyPem = null;
    }
    
    /// <summary>
    /// Generates a new key pair and returns a <see cref="SignatureConfiguration"/> instance using <see cref="SignatureKeyGenerator.Generate()"/>.
    /// </summary>
    /// <returns>A <see cref="SignatureConfiguration"/> instance with the generated key pair.</returns>
    public static SignatureConfiguration Generate()
    {
        return SignatureKeyGenerator.Generate();
    }
}