using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.SimplePlayer;

/// <summary>
/// Example program to play audio, record, and apply effects using the refactored SoundFlow library.
/// </summary>
internal static class Program
{
    private static readonly string RecordedFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recorded.wav");
    private static readonly AudioEngine Engine = new MiniAudioEngine();
    private static readonly AudioFormat Format = AudioFormat.DvdHq;
    
    // Represents detailed configuration for a MiniAudio device, allowing fine-grained control over general and backend-specific settings, Not essential though.
    private static readonly DeviceConfig DeviceConfig =  new MiniAudioDeviceConfig
    {
        PeriodSizeInFrames = 960, // 10ms at 48kHz = 480 frames @ 2 channels = 960 frames
        Playback = new DeviceSubConfig
        {
            ShareMode = ShareMode.Shared // Use shared mode for better compatibility with other applications
        },
        Capture = new DeviceSubConfig
        {
            ShareMode = ShareMode.Shared // Use shared mode for better compatibility with other applications
        },
        Wasapi = new WasapiSettings
        {
            Usage = WasapiUsage.ProAudio // Use ProAudio mode for lower latency on Windows
        }
    };

    private static void Main()
    {
        try
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("\nSoundFlow Example Menu:");
                Console.WriteLine("1. Play Audio From File");
                Console.WriteLine("2. Record and Playback Audio");
                Console.WriteLine("3. Live Microphone Passthrough");
                Console.WriteLine("4. Component and Modifier Tests");
                Console.WriteLine("Press any other key to exit.");

                var choice = Console.ReadKey(true).KeyChar;
                Console.WriteLine();

                switch (choice)
                {
                    case '1':
                        PlayAudioFromFile();
                        break;
                    case '2':
                        RecordAndPlaybackAudio();
                        break;
                    case '3':
                        LiveMicrophonePassthrough();
                        break;
                    case '4':
                        ComponentTests.Run();
                        break;
                    default:
                        Console.WriteLine("Exiting.");
                        return;
                }

                Console.WriteLine("\nOperation complete. Press any key to return to the menu.");
                Console.ReadKey();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn unexpected error occurred: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
        finally
        {
            // Dispose the single engine instance on application exit.
            Engine.Dispose();
        }
    }

    #region Device Selection Helpers

    /// <summary>
    /// Prompts the user to select a single device from a list.
    /// </summary>
    private static DeviceInfo? SelectDevice(DeviceType type)
    {
        Engine.UpdateAudioDevicesInfo();
        var devices = type == DeviceType.Playback ? Engine.PlaybackDevices : Engine.CaptureDevices;

        if (devices.Length == 0)
        {
            Console.WriteLine($"No {type.ToString().ToLower()} devices found.");
            return null;
        }

        Console.WriteLine($"\nPlease select a {type.ToString().ToLower()} device:");
        for (var i = 0; i < devices.Length; i++)
        {
            Console.WriteLine($"  {i}: {devices[i].Name} {(devices[i].IsDefault ? "(Default)" : "")}");
        }

        while (true)
        {
            Console.Write("Enter device index: ");
            if (int.TryParse(Console.ReadLine(), out var index) && index >= 0 && index < devices.Length)
            {
                return devices[index];
            }
            Console.WriteLine("Invalid index. Please try again.");
        }
    }

    #endregion

    #region Menu Options

    private static void PlayAudioFromFile()
    {
        Console.Write("Enter audio file path: ");
        var filePath = Console.ReadLine()?.Replace("\"", "") ?? string.Empty;
        var isNetworked = Uri.TryCreate(filePath, UriKind.Absolute, out var uriResult) 
                          && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        
        if (!isNetworked && !File.Exists(filePath))
        {
            Console.WriteLine("File not found at the specified path.");
            return;
        }
        
        Console.WriteLine(!isNetworked ? "Input is a file path. Opening file stream..." : "Input is a URL. Initializing network stream...");
        
        var deviceInfo = SelectDevice(DeviceType.Playback);
        if (!deviceInfo.HasValue) return;
        
        var playbackDevice = Engine.InitializePlaybackDevice(deviceInfo.Value, Format, DeviceConfig);
        playbackDevice.Start();
        
        using ISoundDataProvider dataProvider = isNetworked ? new NetworkDataProvider(Engine, Format, filePath) : new StreamDataProvider(Engine, Format, new FileStream(filePath, FileMode.Open, FileAccess.Read));
        using var soundPlayer = new SoundPlayer(Engine, Format, dataProvider);
        
        playbackDevice.MasterMixer.AddComponent(soundPlayer);
        soundPlayer.Play();

        PlaybackControls(soundPlayer);

        playbackDevice.MasterMixer.RemoveComponent(soundPlayer);
        playbackDevice.Stop();
        playbackDevice.Dispose();
    }
    
    private static void LiveMicrophonePassthrough()
    {
        var captureDeviceInfo = SelectDevice(DeviceType.Capture);
        if (!captureDeviceInfo.HasValue) return;

        var playbackDeviceInfo = SelectDevice(DeviceType.Playback);
        if (!playbackDeviceInfo.HasValue) return;

        using var duplexDevice = Engine.InitializeFullDuplexDevice(playbackDeviceInfo.Value, captureDeviceInfo.Value, Format, DeviceConfig);
        
        duplexDevice.Start();
        
        using var microphoneProvider = new MicrophoneDataProvider(duplexDevice);
        using var soundPlayer = new SoundPlayer(Engine, Format, microphoneProvider);
        
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

    private static void RecordAndPlaybackAudio()
    {
        var captureDeviceInfo = SelectDevice(DeviceType.Capture);
        if (!captureDeviceInfo.HasValue) return;

        using var captureDevice = Engine.InitializeCaptureDevice(captureDeviceInfo.Value, Format, DeviceConfig);
        captureDevice.Start();
        
        var stream = new FileStream(RecordedFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using (var recorder = new Recorder(captureDevice, stream))
        {
            Console.WriteLine("Recording started. Press 's' to stop, 'p' to pause/resume.");
            recorder.StartRecording();

            while (recorder.State != PlaybackState.Stopped)
            {
                var key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.S:
                        recorder.StopRecording();
                        break;
                    case ConsoleKey.P:
                        if (recorder.State == PlaybackState.Paused)
                        {
                            recorder.ResumeRecording();
                            Console.WriteLine("Recording resumed.");
                        }
                        else
                        {
                            recorder.PauseRecording();
                            Console.WriteLine("Recording paused.");
                        }
                        break;
                }
            }
        }
        
        stream.Dispose();
        captureDevice.Stop();

        Console.WriteLine($"\nRecording finished. File saved to: {RecordedFilePath}");
        Console.WriteLine("Press 'p' to play back or any other key to skip.");
        if (Console.ReadKey(true).Key != ConsoleKey.P) return;

        // Playback
        var playbackDeviceInfo = SelectDevice(DeviceType.Playback);
        if (!playbackDeviceInfo.HasValue) return;

        using var playbackDevice = Engine.InitializePlaybackDevice(playbackDeviceInfo.Value, Format, DeviceConfig);
        playbackDevice.Start();

        using var dataProvider = new StreamDataProvider(Engine, Format, new FileStream(RecordedFilePath, FileMode.Open, FileAccess.Read));
        using var soundPlayer = new SoundPlayer(Engine, Format, dataProvider);

        playbackDevice.MasterMixer.AddComponent(soundPlayer);
        soundPlayer.Play();

        PlaybackControls(soundPlayer);

        playbackDevice.MasterMixer.RemoveComponent(soundPlayer);
        playbackDevice.Stop();
    }

    #endregion

    #region Playback Controls UI
    
    private static void PlaybackControls(ISoundPlayer player)
    {
        Console.WriteLine("\n--- Playback Controls ---");
        Console.WriteLine("'P': Play/Pause | 'S': Seek | 'V': Volume | '+/-': Speed | 'R': Reset Speed | Any other: Stop");
        
        using var timer = new System.Timers.Timer(500);
        timer.AutoReset = true;
        timer.Elapsed += (_, _) =>
        {
            if (player.State != PlaybackState.Stopped)
            {
                Console.Write($"\rTime: {TimeSpan.FromSeconds(player.Time):mm\\:ss\\.ff} / {TimeSpan.FromSeconds(player.Duration):mm\\:ss\\.ff} | Speed: {player.PlaybackSpeed:F1}x | Vol: {player.Volume:F1}  ");
            }
        };
        timer.Start();
        
        while (player.State is PlaybackState.Playing or PlaybackState.Paused)
        {
            var keyInfo = Console.ReadKey(true);
            switch (keyInfo.Key)
            {
                case ConsoleKey.P:
                    if (player.State == PlaybackState.Playing) player.Pause();
                    else player.Play();
                    break;
                case ConsoleKey.S:
                    Console.Write("\nEnter seek time in seconds (e.g., 5.0): ");
                    if (float.TryParse(Console.ReadLine(), out var seekTime)) player.Seek(TimeSpan.FromSeconds(seekTime));
                    else Console.WriteLine("Invalid seek time.");
                    break;
                case ConsoleKey.OemPlus or ConsoleKey.Add:
                    player.PlaybackSpeed = Math.Min(player.PlaybackSpeed + 0.1f, 4.0f);
                    break;
                case ConsoleKey.OemMinus or ConsoleKey.Subtract:
                    player.PlaybackSpeed = Math.Max(0.1f, player.PlaybackSpeed - 0.1f);
                    break;
                case ConsoleKey.R:
                    player.PlaybackSpeed = 1.0f;
                    break;
                case ConsoleKey.V:
                    Console.Write("\nEnter volume (0.0 to 2.0): ");
                    if (float.TryParse(Console.ReadLine(), out var volume))
                        player.Volume = Math.Clamp(volume, 0.0f, 2.0f);
                    else
                        Console.WriteLine("Invalid volume.");
                    break;
                default:
                    player.Stop();
                    break;
            }
        }

        timer.Stop();
        Console.WriteLine("\nPlayback stopped.                ");
    }

    #endregion
}