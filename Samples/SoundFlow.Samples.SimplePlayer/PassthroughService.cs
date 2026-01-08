using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.SimplePlayer;

/// <summary>
/// Encapsulates logic for live microphone audio passthrough.
/// </summary>
public static class PassthroughService
{
    /// <summary>
    /// Initializes and runs a full-duplex audio stream, piping microphone input to the output.
    /// </summary>
    public static void Run(AudioEngine engine, AudioFormat format, DeviceConfig deviceConfig)
    {
        var captureDeviceInfo = DeviceService.SelectDevice(engine, DeviceType.Capture);
        if (!captureDeviceInfo.HasValue) return;

        var playbackDeviceInfo = DeviceService.SelectDevice(engine, DeviceType.Playback);
        if (!playbackDeviceInfo.HasValue) return;

        using var duplexDevice = engine.InitializeFullDuplexDevice(playbackDeviceInfo.Value, captureDeviceInfo.Value, format, deviceConfig);
        
        duplexDevice.Start();
        
        using var microphoneProvider = new MicrophoneDataProvider(duplexDevice);
        using var soundPlayer = new SoundPlayer(engine, format, microphoneProvider);
        
        duplexDevice.MasterMixer.AddComponent(soundPlayer);
        
        microphoneProvider.StartCapture();
        soundPlayer.Play();
        
        Console.WriteLine("\nLive microphone passthrough is active. Press any key to stop.");
        Console.ReadKey();
        
        microphoneProvider.StopCapture();
        soundPlayer.Stop();
        
        duplexDevice.MasterMixer.RemoveComponent(soundPlayer);
        
        duplexDevice.Stop();
    }
}