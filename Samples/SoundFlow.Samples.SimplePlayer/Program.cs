using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Structs;

namespace SoundFlow.Samples.SimplePlayer;

/// <summary>
/// A menu-driven example program to demonstrate core SoundFlow features.
/// </summary>
internal static class Program
{
    private static readonly string RecordedFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recorded.wav");
    private static readonly AudioEngine Engine = new MiniAudioEngine();
    private static readonly AudioFormat Format = AudioFormat.DvdHq;
    
    // Represents detailed configuration for a MiniAudio device, allowing fine-grained control over general and backend-specific settings.
    private static readonly DeviceConfig DeviceConfig =  new MiniAudioDeviceConfig
    {
        PeriodSizeInFrames = 9600, // 10ms at 48kHz = 480 frames @ 2 channels = 960 frames
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
                        PlaybackService.PlayFromUserInput(Engine, Format, DeviceConfig);
                        break;
                    case '2':
                        RecordingService.RecordAndPlayback(Engine, Format, DeviceConfig, RecordedFilePath);
                        break;
                    case '3':
                        PassthroughService.Run(Engine, Format, DeviceConfig);
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
}