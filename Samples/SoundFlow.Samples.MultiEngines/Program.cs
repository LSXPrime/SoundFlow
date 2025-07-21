using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.MultiEngines;

/// <summary>
/// Example program to demonstrate multi-device capabilities of SoundFlow.
/// </summary>
internal static class Program
{
    private static readonly AudioEngine Engine = new MiniAudioEngine();
    private static readonly AudioFormat Format = AudioFormat.DvdHq;
    private static readonly string RecordingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recordings");

    private static void Main()
    {
        try
        {
            Directory.CreateDirectory(RecordingDirectory);

            while (true)
            {
                Console.Clear();
                Console.WriteLine("\nSoundFlow Multi-Device Example Menu:");
                Console.WriteLine("1. Play Audio From File on Multiple Devices");
                Console.WriteLine("2. Record from Multiple Microphones & Playback");
                Console.WriteLine("3. Live Passthrough from Multiple Microphones to Multiple Speakers");
                Console.WriteLine("Press any other key to exit.");

                var choice = Console.ReadKey(true).KeyChar;
                Console.WriteLine();

                switch (choice)
                {
                    case '1':
                        PlayAudioFromFileOnMultipleDevices();
                        break;
                    case '2':
                        RecordFromMultipleAndPlayback();
                        break;
                    case '3':
                        LiveMultiMicToMultiSpeakerPassthrough();
                        break;
                    default:
                        Console.WriteLine("Exiting.");
                        return;
                }

                Console.WriteLine("\nOperation complete. Press any key to return to the menu.");
                Console.ReadKey();
                Console.Clear();
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
            Engine.Dispose();
        }
    }

    #region Menu Options

    private static void PlayAudioFromFileOnMultipleDevices()
    {
        Console.Write("Enter audio file path: ");
        var filePath = Console.ReadLine()?.Replace("\"", "") ?? string.Empty;

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return;
        }

        var selectedDevices = SelectMultipleDevices(DeviceType.Playback);
        if (selectedDevices.Count == 0)
        {
            Console.WriteLine("No playback devices selected.");
            return;
        }

        var playbackDevices = new List<AudioPlaybackDevice>();
        var soundPlayers = new List<ISoundPlayer>();

        try
        {
            // Create a separate player and data provider for each device to ensure independent playback.
            foreach (var devInfo in selectedDevices)
            {
                var device = Engine.InitializePlaybackDevice(devInfo, Format);
                playbackDevices.Add(device);

                var provider = new StreamDataProvider(Engine, Format, File.OpenRead(filePath));
                var player = new SoundPlayer(Engine, Format, provider);
                soundPlayers.Add(player);

                device.MasterMixer.AddComponent(player);
                device.Start();
                player.Play();
                Console.WriteLine($"Started playback on: {device.Info?.Name}");
            }
            
            if (soundPlayers.Any())
            {
                Console.WriteLine($"\nPlaying audio on {playbackDevices.Count} device(s).");
                PlaybackControls(soundPlayers);
            }
        }
        finally
        {
            soundPlayers.ForEach(p => p.Dispose());
            playbackDevices.ForEach(d =>
            {
                d.Stop();
                d.Dispose();
            });
        }
    }

    private static void RecordFromMultipleAndPlayback()
    {
        Console.WriteLine("Select one or more microphones to record from.");
        var selectedCaptureDevices = SelectMultipleDevices(DeviceType.Capture);
        if (selectedCaptureDevices.Count == 0)
        {
            Console.WriteLine("No capture devices selected.");
            return;
        }

        var captureDevices = new List<AudioCaptureDevice>();
        var recorders = new List<Recorder>();
        var streams = new List<FileStream>();
        var recordedFiles = new List<string>();

        try
        {
            foreach (var (devInfo, i) in selectedCaptureDevices.Select((dev, i) => (dev, i)))
            {
                // Correctly initialize each capture device.
                var device = Engine.InitializeCaptureDevice(devInfo, Format); 
                captureDevices.Add(device);

                var filePath = Path.Combine(RecordingDirectory, $"record_{i}_{SanitizeFileName(devInfo.Name)}.wav");
                recordedFiles.Add(filePath);
                
                var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                streams.Add(stream);
                
                var recorder = new Recorder(device, stream);
                recorders.Add(recorder);
            }

            captureDevices.ForEach(d => d.Start());
            recorders.ForEach(r => r.StartRecording());

            Console.WriteLine($"\nRecording from {recorders.Count} device(s). Press 's' to stop.");
            while (Console.ReadKey(true).Key != ConsoleKey.S) { Thread.Sleep(100); }

            recorders.ForEach(r => r.StopRecording());
            Console.WriteLine("\nRecording finished.");
        }
        finally
        {
            recorders.ForEach(r => r.Dispose());
            streams.ForEach(s => s.Dispose());
            captureDevices.ForEach(d => { d.Stop(); d.Dispose(); });
        }

        Console.WriteLine("\nRecorded files:");
        recordedFiles.ForEach(Console.WriteLine);
        Console.WriteLine("Press 'p' to play back all recorded files sequentially, or any other key to skip.");
        if (Console.ReadKey(true).Key != ConsoleKey.P) return;
        
        PlayMultipleFilesSequentially(recordedFiles);
    }

    private static void LiveMultiMicToMultiSpeakerPassthrough()
    {
        Console.WriteLine("Select one or more capture devices for microphone input.");
        var selectedCaptureDevices = SelectMultipleDevices(DeviceType.Capture);
        if (selectedCaptureDevices.Count == 0) return;

        Console.WriteLine("\nNow, select one or more devices for playback.");
        var selectedPlaybackDevices = SelectMultipleDevices(DeviceType.Playback);
        if (selectedPlaybackDevices.Count == 0) return;
        
        var captureDevices = selectedCaptureDevices.Select(d => Engine.InitializeCaptureDevice(d, Format)).ToList();
        var playbackDevices = selectedPlaybackDevices.Select(d => Engine.InitializePlaybackDevice(d, Format)).ToList();
        var allComponents = new List<IDisposable>();

        try
        {
            // For each output device, create a dedicated mixer and a full set of input components.
            foreach (var playbackDevice in playbackDevices)
            {
                var deviceInputMixer = new Mixer(Engine, Format);
                allComponents.Add(deviceInputMixer);
                playbackDevice.MasterMixer.AddComponent(deviceInputMixer);

                // Create a dedicated provider and player for each mic, for THIS specific output device.
                foreach (var captureDevice in captureDevices)
                {
                    var provider = new MicrophoneDataProvider(captureDevice);
                    allComponents.Add(provider);
                    
                    var player = new SoundPlayer(Engine, Format, provider);
                    allComponents.Add(player);

                    deviceInputMixer.AddComponent(player);
                    provider.StartCapture();
                    player.Play();
                }
            }

            captureDevices.ForEach(d => d.Start());
            playbackDevices.ForEach(d => d.Start());

            Console.WriteLine("\nLive multi-microphone passthrough is active. Press any key to stop.");
            Console.ReadKey();
        }
        finally
        {
            // Dispose everything in reverse order of creation.
            allComponents.Reverse();
            allComponents.ForEach(c => c.Dispose());
            captureDevices.ForEach(d => { d.Stop(); d.Dispose(); });
            playbackDevices.ForEach(d => { d.Stop(); d.Dispose(); });
        }
    }
    
    #endregion
    
    #region UI and Playback Helpers

    private static void PlayMultipleFilesSequentially(List<string> files)
    {
        if (!files.Any()) return;

        Console.WriteLine("\nSelect devices for sequential playback.");
        var selectedPlaybackDevices = SelectMultipleDevices(DeviceType.Playback);
        if (selectedPlaybackDevices.Count == 0) return;
        
        var playbackDevices = selectedPlaybackDevices.Select(d => Engine.InitializePlaybackDevice(d, Format)).ToList();
        try
        {
            playbackDevices.ForEach(d => d.Start());

            foreach (var file in files)
            {
                if (!File.Exists(file)) continue;
                
                Console.WriteLine($"\nNow playing: {Path.GetFileName(file)}");
                
                SoundPlayer player = null!;
                
                // Route this single player to all selected output devices
                playbackDevices.ForEach(d =>
                {
                    // Create a single player and provider for this file
                    using var provider = new StreamDataProvider(Engine, Format, new FileStream(file, FileMode.Open, FileAccess.Read));
                    player = new SoundPlayer(Engine, Format, provider);
                    
                    d.MasterMixer.AddComponent(player);
                    player.Play();
                    
                });
                
                PlaybackControls([player]); // Control this single player
                
                // Clean up the player from all mixers before the next loop iteration
                playbackDevices.ForEach(d => d.MasterMixer.RemoveComponent(player));
            }
        }
        finally
        {
            playbackDevices.ForEach(d => { d.Stop(); d.Dispose(); });
        }
    }
    
    private static List<DeviceInfo> SelectMultipleDevices(DeviceType type)
    {
        var selectedDevices = new List<DeviceInfo>();
        while (true)
        {
            Console.Clear();
            Engine.UpdateDevicesInfo();
            var availableDevices = type == DeviceType.Playback ? Engine.PlaybackDevices : Engine.CaptureDevices;

            if (availableDevices.Length == 0)
            {
                Console.WriteLine($"No {type} devices found. Press any key to continue.");
                Console.ReadKey();
                return selectedDevices;
            }

            Console.WriteLine($"\n--- Available {type} Devices ---");
            for (var i = 0; i < availableDevices.Length; i++)
                Console.WriteLine($"  {i}: {availableDevices[i].Name}");

            Console.WriteLine("\n--- Currently Selected Devices ---");
            if (selectedDevices.Any())
                selectedDevices.ForEach(dev => Console.WriteLine($"  - {dev.Name}"));
            else
                Console.WriteLine("  (None)");

            Console.WriteLine("\n--- Options ---");
            Console.WriteLine("  'a' - Add a device by index");
            Console.WriteLine("  'd' - Deselect all devices");
            Console.WriteLine("  's' - Continue with selected devices");
            Console.WriteLine("  'x' - Cancel");
            Console.Write("\nEnter your choice: ");
            
            var keyChar = Console.ReadKey(true).KeyChar;
            switch (char.ToLower(keyChar))
            {
                case 'a':
                    Console.Write("\nEnter device index to add: ");
                    if (int.TryParse(Console.ReadLine(), out var index) && index >= 0 && index < availableDevices.Length)
                    {
                        var deviceToAdd = availableDevices[index];
                        if (!selectedDevices.Contains(deviceToAdd)) selectedDevices.Add(deviceToAdd);
                    }
                    else Console.WriteLine("Invalid index.");
                    break;
                case 'd':
                    selectedDevices.Clear();
                    break;
                case 's':
                    if (selectedDevices.Any()) return selectedDevices;
                    Console.WriteLine("\nPlease select at least one device.");
                    break;
                case 'x':
                    return [];
            }
        }
    }

    private static void PlaybackControls(IReadOnlyList<ISoundPlayer> players)
    {
        if (players.Count == 0) return;
        var firstPlayer = players[0];

        Console.WriteLine("\n--- Playback Controls (affects all players) ---");
        Console.WriteLine("'P': Play/Pause | 'S': Seek | Any other: Stop");
        
        using var timer = new System.Timers.Timer(250);
        timer.AutoReset = true;
        timer.Elapsed += (_, _) =>
        {
            if (firstPlayer.State is PlaybackState.Playing or PlaybackState.Paused)
            {
                Console.Write($"\rTime: {TimeSpan.FromSeconds(firstPlayer.Time):mm\\:ss} / {TimeSpan.FromSeconds(firstPlayer.Duration):mm\\:ss}");
            }
        };
        timer.Start();

        while (firstPlayer.State != PlaybackState.Stopped)
        {
            if (!Console.KeyAvailable)
            {
                Thread.Sleep(50);
                continue;
            }
            
            var keyInfo = Console.ReadKey(true);
            switch (keyInfo.Key)
            {
                case ConsoleKey.P:
                    players.ToList().ForEach(p => { if (p.State == PlaybackState.Playing) p.Pause(); else p.Play(); });
                    break;
                case ConsoleKey.S:
                    Console.Write("\nEnter seek time in seconds: ");
                    if (float.TryParse(Console.ReadLine(), out var seekTime))
                    {
                        players.ToList().ForEach(p => p.Seek(TimeSpan.FromSeconds(seekTime)));
                    }
                    else Console.WriteLine("Invalid seek time.");
                    break;
                default:
                    players.ToList().ForEach(p => p.Stop());
                    break;
            }
        }
        timer.Stop();
        Console.WriteLine("\nPlayback stopped.                ");
    }

    private static string SanitizeFileName(string name)
    {
        return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    }

    #endregion
}