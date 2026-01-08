using System.Security.Cryptography;
using SoundFlow.Security.Configuration;

namespace SoundFlow.Security.Utils;


/// <summary>
/// Utility for generating secure ECDSA key pairs for file signing.
/// </summary>
public static class SignatureKeyGenerator
{
    /// <summary>
    /// Generates a new ECDSA key pair using the NIST P-384 curve.
    /// P-384 offers a security level approximately equivalent to AES-192.
    /// </summary>
    /// <returns>A <see cref="SignatureConfiguration"/> containing the Private Key (PKCS#8) and the Public Key (SPKI).</returns>
    public static SignatureConfiguration Generate()
    {
        using var algorithm = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        
        // Export private key in PKCS#8 format (standard for private keys)
        var privateKey = algorithm.ExportPkcs8PrivateKeyPem();
        
        // Export public key in SubjectPublicKeyInfo format (standard for x.509/public keys)
        var publicKey = algorithm.ExportSubjectPublicKeyInfoPem();

        return new SignatureConfiguration(privateKey, publicKey);
    }
}