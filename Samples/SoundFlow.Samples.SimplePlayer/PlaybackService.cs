using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.SimplePlayer;

/// <summary>
/// Encapsulates all logic related to audio playback.
/// </summary>
public static class PlaybackService
{
    /// <summary>
    /// Prompts the user for a file path (local or URL) and initiates playback.
    /// </summary>
    public static void PlayFromUserInput(AudioEngine engine, AudioFormat format, DeviceConfig deviceConfig)
    {
        Console.Write("Enter audio file path or URL: ");
        var filePath = Console.ReadLine()?.Trim().Replace("\"", "") ?? string.Empty;
        
        var isNetworked = Uri.TryCreate(filePath, UriKind.Absolute, out var uriResult) 
                          && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        
        if (!isNetworked && !File.Exists(filePath))
        {
            Console.WriteLine("File not found at the specified path.");
            return;
        }
        
        PlayFile(engine, format, deviceConfig, filePath, isNetworked);
    }
    
    /// <summary>
    /// Plays a specified audio file or URL.
    /// </summary>
    public static void PlayFile(AudioEngine engine, AudioFormat format, DeviceConfig deviceConfig, string path, bool isNetworked = false)
    {
        Console.WriteLine(!isNetworked ? "Input is a file path. Opening file stream..." : "Input is a URL. Initializing network stream...");
        
        var deviceInfo = DeviceService.SelectDevice(engine, DeviceType.Playback);
        if (!deviceInfo.HasValue) return;

        using var playbackDevice = engine.InitializePlaybackDevice(deviceInfo.Value, format, deviceConfig);
        playbackDevice.Start();
        
        using ISoundDataProvider dataProvider = isNetworked 
            ? new NetworkDataProvider(engine, format, path) 
            : new StreamDataProvider(engine, format, new FileStream(path, FileMode.Open, FileAccess.Read));
        
        using var soundPlayer = new SoundPlayer(engine, format, dataProvider);
        
        playbackDevice.MasterMixer.AddComponent(soundPlayer);
        soundPlayer.Play();

        UserInterfaceService.DisplayPlaybackControls(soundPlayer);

        playbackDevice.MasterMixer.RemoveComponent(soundPlayer);
        playbackDevice.Stop();
    }
}