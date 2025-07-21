using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Experimental;
using SoundFlow.Extensions.WebRtc.Apm;
using SoundFlow.Extensions.WebRtc.Apm.Components;
using SoundFlow.Extensions.WebRtc.Apm.Modifiers;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.NoiseSuppression;

/// <summary>
/// Example program demonstrating audio processing like noise suppression using SoundFlow.
/// </summary>
internal static class Program
{
    private static readonly AudioEngine Engine = new MiniAudioEngine();
    private static readonly AudioFormat Format = AudioFormat.Dvd;
    private static readonly string CleanedFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cleaned-audio.wav");

    private static void Main()
    {
        try
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("\nSoundFlow Noise Suppression Example Menu:");
                Console.WriteLine("1. Play Audio From File with Noise Suppression");
                Console.WriteLine("2. Live Microphone Passthrough with Noise Suppression");
                Console.WriteLine("3. Clean Audio File From Noise (Offline Processing)");
                Console.WriteLine("Press any other key to exit.");

                var choice = Console.ReadKey(true).KeyChar;
                Console.WriteLine();

                switch (choice)
                {
                    case '1':
                        PlayAudioFromFileWithSuppression();
                        break;
                    case '2':
                        LivePassthroughWithSuppression();
                        break;
                    case '3':
                        CleanAudioFileFromNoise();
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
        Engine.UpdateDevicesInfo();
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

    /// <summary>
    /// Plays an audio file with real-time noise suppression applied.
    /// </summary>
    private static void PlayAudioFromFileWithSuppression()
    {
        Console.Write("Enter audio file path: ");
        var filePath = Console.ReadLine()?.Replace("\"", "") ?? string.Empty;

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return;
        }

        var playbackDeviceInfo = SelectDevice(DeviceType.Playback);
        if (!playbackDeviceInfo.HasValue) return;

        using var playbackDevice = Engine.InitializePlaybackDevice(playbackDeviceInfo.Value, Format);
        playbackDevice.Start();

        using var dataProvider = new StreamDataProvider(Engine, Format, new FileStream(filePath, FileMode.Open, FileAccess.Read));
        using var soundPlayer = new SoundPlayer(Engine, Format, dataProvider);

        soundPlayer.AddModifier(new VoiceIsolationEffect(Format.SampleRate));
        
        // Add a modifier to the player to apply noise suppression.
        soundPlayer.AddModifier(new WebRtcApmModifier(device: playbackDevice, nsEnabled: true, nsLevel: NoiseSuppressionLevel.VeryHigh));

        playbackDevice.MasterMixer.AddComponent(soundPlayer);
        soundPlayer.Play();

        Console.WriteLine("\nPlayback started with noise suppression. Press any key to stop.");
        Console.ReadKey();

        soundPlayer.Stop();
        playbackDevice.MasterMixer.RemoveComponent(soundPlayer);
        playbackDevice.Stop();
    }

    /// <summary>
    /// Captures audio from a microphone, applies noise suppression, and plays it back in real-time.
    /// </summary>
    private static void LivePassthroughWithSuppression()
    {
        var captureDeviceInfo = SelectDevice(DeviceType.Capture);
        if (!captureDeviceInfo.HasValue) return;

        var playbackDeviceInfo = SelectDevice(DeviceType.Playback);
        if (!playbackDeviceInfo.HasValue) return;

        using var captureDevice = Engine.InitializeCaptureDevice(captureDeviceInfo.Value, Format);
        using var playbackDevice = Engine.InitializePlaybackDevice(playbackDeviceInfo.Value, Format);

        captureDevice.Start();
        playbackDevice.Start();

        using var microphoneProvider = new MicrophoneDataProvider(captureDevice);
        using var soundPlayer = new SoundPlayer(Engine, Format, microphoneProvider);

        // Add noise suppression modifier and keep a reference to it for interactive controls.
        var apmModifier = new WebRtcApmModifier(captureDevice, nsEnabled: true, nsLevel: NoiseSuppressionLevel.VeryHigh);
        soundPlayer.AddModifier(apmModifier);

        playbackDevice.MasterMixer.AddComponent(soundPlayer);

        microphoneProvider.StartCapture();
        soundPlayer.Play();

        Console.WriteLine("\nLive passthrough active with noise suppression.");
        Console.WriteLine("'S': Toggle Suppression | 'L': Cycle Level | Any other key: Stop");

        var numberOfLevels = Enum.GetValues(typeof(NoiseSuppressionLevel)).Length;
        while (true)
        {
            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.S)
            {
                apmModifier.NoiseSuppression.Enabled = !apmModifier.NoiseSuppression.Enabled;
                Console.WriteLine($"Noise suppression enabled: {apmModifier.NoiseSuppression.Enabled}");
            }
            else if (key == ConsoleKey.L)
            {
                var nextLevel = (NoiseSuppressionLevel)(((int)apmModifier.NoiseSuppression.Level + 1) % numberOfLevels);
                apmModifier.NoiseSuppression.Level = nextLevel;
                Console.WriteLine($"Noise suppression level: {apmModifier.NoiseSuppression.Level}");
            }
            else
            {
                break;
            }
        }

        microphoneProvider.StopCapture();
        soundPlayer.Stop();

        playbackDevice.MasterMixer.RemoveComponent(soundPlayer);

        captureDevice.Stop();
        playbackDevice.Stop();
    }

    /// <summary>
    /// Processes an audio file to remove noise and saves the result to a new file (offline).
    /// </summary>
    private static void CleanAudioFileFromNoise()
    {
        Console.Write("Enter path to noisy audio file: ");
        var filePath = Console.ReadLine()?.Replace("\"", "") ?? string.Empty;

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return;
        }
        
        // NOTE: The NoiseSuppressor component is designed for offline file-to-file processing.
        Console.WriteLine("\nProcessing... This may take a moment for large files.");
        
        using var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        
        // For offline processing, a device-independent data provider is used.
        using var dataProvider = new StreamDataProvider(Engine, Format,  inputStream); 
        
        using var noiseSuppressor = new NoiseSuppressor(
            dataProvider: dataProvider,
            audioFormat: Format,
            suppressionLevel: NoiseSuppressionLevel.VeryHigh
        );

        // Process the entire data stream in memory.
        var cleanData = noiseSuppressor.ProcessAll();

        using var outputStream = new FileStream(CleanedFilePath, FileMode.Create, FileAccess.Write);
        using var encoder = Engine.CreateEncoder(outputStream, EncodingFormat.Wav, Format);
        
        encoder.Encode(cleanData.AsSpan());

        Console.WriteLine($"Processing complete. Cleaned audio saved to: {CleanedFilePath}");
    }

    #endregion
}