using SoundFlow.Abstracts;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Metadata.Models;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;

namespace SoundFlow.Samples.Recording;

/// <summary>
/// Manages the recording session, applying configuration and handling state.
/// </summary>
public static class RecordingService
{
    /// <summary>
    /// Starts a recording session.
    /// </summary>
    /// <param name="engine">The audio engine.</param>
    /// <param name="deviceInfo">The selected input device.</param>
    /// <param name="outputPath">The file path to record to.</param>
    /// <param name="signingConfig">Optional configuration for digital signing.</param>
    /// <param name="tags">Optional metadata tags.</param>
    public static async Task RecordAsync(
        AudioEngine engine, 
        DeviceInfo deviceInfo, 
        string outputPath, 
        SignatureConfiguration? signingConfig, 
        SoundTags? tags)
    {
        // 1. Initialize Capture Device (Standard CD Quality)
        var format = new AudioFormat
        {
            SampleRate = 48000,
            Channels = 1,
            Format = SampleFormat.F32,
            Layout = ChannelLayout.Mono
        };

        using var captureDevice = engine.InitializeCaptureDevice(deviceInfo, format);
        
        // 2. Setup Recorder
        // Using "wav" format. Could be "mp3" or "flac" if codecs are registered.
        using var recorder = new Recorder(captureDevice, outputPath, "wav");
        
        // Apply signing configuration if provided
        recorder.SigningConfiguration = signingConfig;

        // 3. Start Recording
        Console.WriteLine($"\nInitializing recording to '{outputPath}'...");
        captureDevice.Start();
        
        var result = recorder.StartRecording(tags);
        if (!result.IsSuccess)
        {
            Console.WriteLine($"Failed to start recording: {result.Error}");
            return;
        }
        
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(" >> RECORDING STARTED << ");
        Console.ResetColor();
        Console.WriteLine("Press any key to stop...");

        // 4. Monitoring Loop
        // In a real app, you might attach an AudioAnalyzer (e.g., LevelMeter) here 
        // to show VU meters in the console.
        while (!Console.KeyAvailable)
        {
            await Task.Delay(100);
        }
        Console.ReadKey(true); // Consume the key press

        // 5. Stop Recording
        // This triggers:
        // a) Encoder finalization
        // b) Metadata writing (SoundTags)
        // c) Digital Signing (if configured)
        Console.WriteLine("\nStopping recording...");
        result = await recorder.StopRecordingAsync();
        if (!result.IsSuccess)
        {
            Console.WriteLine($"Failed to stop recording: {result.Error}");
            return;
        }
        
        captureDevice.Stop();
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Recording saved.");
        Console.ResetColor();
    }
}