using SoundFlow.Abstracts;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Security;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;

namespace SoundFlow.Samples.Security.Encryption;

/// <summary>
/// Encapsulates the logic for real-time playback of an encrypted audio stream.
/// </summary>
public static class PlaybackService
{
    /// <summary>
    /// Plays an encrypted SoundFlow container file in real-time.
    /// Optionally verifies the digital signature before playback starts.
    /// </summary>
    /// <param name="engine">The audio engine for playback.</param>
    /// <param name="encryptedFile">The path to the encrypted container file.</param>
    /// <param name="secretKey">The 32-byte secret key used for encryption.</param>
    /// <param name="publicKey">The PEM-encoded public key. If provided, signature verification is enforced.</param>
    public static async Task PlayEncryptedStreamAsync(AudioEngine engine, string encryptedFile, byte[] secretKey, string? publicKey = null)
    {
        Console.WriteLine("Preparing for real-time encrypted playback...");

        Result<ISoundDataProvider> providerResult;
        var fileStream = new FileStream(encryptedFile, FileMode.Open, FileAccess.Read);

        if (!string.IsNullOrEmpty(publicKey))
        {
            string? signature = null;
            var sigPath = encryptedFile + ".sig";
            
            // Check for detached signature
            if (File.Exists(sigPath))
            {
                Console.WriteLine("Found detached signature file.");
                signature = await File.ReadAllTextAsync(sigPath);
            }
            else
            {
                Console.WriteLine("No detached signature found. Checking for embedded signature...");
            }

            Console.WriteLine("Verifying signature...");
            var signConfig = new SignatureConfiguration { PublicKeyPem = publicKey };
            
            // This handles verification for both embedded (signature=null) and detached cases.
            providerResult = await AudioEncryptor.VerifyAndDecryptAsync(fileStream, secretKey, signConfig, signature);
        }
        else
        {
            providerResult = AudioEncryptor.Decrypt(fileStream, secretKey);
        }
        
        if (providerResult.IsFailure)
        {
            await fileStream.DisposeAsync();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error reading/verifying container: {providerResult.Error?.Message}");
            Console.ResetColor();
            return;
        }

        // The provider now owns the fileStream and will dispose it when the provider is disposed.
        using var provider = providerResult.Value!;
        
        var format = new AudioFormat
        {
            SampleRate = provider.SampleRate,
            Channels = provider.FormatInfo?.ChannelCount ?? 2,
            Format = SampleFormat.F32,
            Layout = AudioFormat.GetLayoutFromChannels(provider.FormatInfo?.ChannelCount ?? 2)
        };

        using var device = engine.InitializePlaybackDevice(null, format);
        var player = new SoundPlayer(engine, format, provider);
        device.MasterMixer.AddComponent(player);
        
        Console.WriteLine("Starting playback...");
        device.Start();
        player.Play();

        Console.WriteLine("Press any key to stop playback...");
        while (player.State != PlaybackState.Stopped && !Console.KeyAvailable)
        {
            await Task.Delay(100);
        }
        
        if (Console.KeyAvailable) Console.ReadKey(true);

        player.Stop();
        device.Stop();
        Console.WriteLine("Playback stopped.");
    }
}