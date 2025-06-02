using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Extensions.WebRtc.Apm;
using SoundFlow.Extensions.WebRtc.Apm.Components;
using SoundFlow.Extensions.WebRtc.Apm.Modifiers;
using SoundFlow.Providers;

namespace SoundFlow.Samples.NoiseSuppression;

class Program
{
    private static AudioEngine? _audioEngine;
    private static readonly string CleanedFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cleaned-audio.wav");
    
    private static void Main()
    {
        SetOrCreateEngine();

        while (true)
        {
            Console.WriteLine("\nChoose an option:");
            Console.WriteLine("1. Play audio from file");
            Console.WriteLine("2. Play audio from microphone");
            Console.WriteLine("3. Clean audio file from noise");
            Console.WriteLine("Press any other key to exit.");

            var choice = Console.ReadKey().KeyChar;
            Console.WriteLine();

            switch (choice)
            {
                case '1':
                    PlayAudioFromFile();
                    break;
                case '2':
                    MixedRecordAndPlayback();
                    break;
                case '3':
                    CleanAudioFileFromNoise();
                    break;
                default:
                    Console.WriteLine("Exiting.");
                    return;
            }

            Console.WriteLine("\nPress any key to continue or 'X' to exit.");
            if (Console.ReadKey().Key == ConsoleKey.X)
                break;
        }

        // Dispose audio engine on exit
        _audioEngine?.Dispose();
    }

    private static void SetOrCreateEngine(Capability capability = Capability.Playback, int sampleRate = 48000,
        SampleFormat sampleFormat = SampleFormat.F32, int channels = 1)
    {
        if (_audioEngine == null || _audioEngine.IsDisposed)
        {
            _audioEngine = new MiniAudioEngine(sampleRate, capability, sampleFormat, channels);
        }
        else if ((_audioEngine.Capability & capability) != capability || _audioEngine.SampleRate != sampleRate ||
                 _audioEngine.SampleFormat != sampleFormat || AudioEngine.Channels != channels)
        {
            _audioEngine.Dispose();
            _audioEngine = new MiniAudioEngine(sampleRate, capability, sampleFormat, channels);
        }
    }
    
    private static void PlayAudioFromFile()
    {
        Console.Write("Enter noisy speech file path: ");
        var filePath = Console.ReadLine()?.Replace("\"", "") ?? string.Empty;

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return;
        }

        Console.WriteLine();

        var dataProvider = new StreamDataProvider(new FileStream(filePath, FileMode.Open, FileAccess.Read));
        var soundPlayer = new SoundPlayer(dataProvider);
        soundPlayer.PlaybackEnded += (_, _) => Console.WriteLine("Playback ended, Press any key to continue."); 
        
        // Add noise suppression modifiers
        soundPlayer.AddModifier(new WebRtcApmModifier(nsEnabled: true, nsLevel: NoiseSuppressionLevel.VeryHigh));
        
        // Add sound player to the master mixer & play
        Mixer.Master.AddComponent(soundPlayer);
        soundPlayer.Play();
        
        Console.WriteLine("Noise suppression applied. Press any key to stop playback.");
        Console.ReadLine();
        
        // Dispose sound player
        soundPlayer.Stop();
        Mixer.Master.RemoveComponent(soundPlayer);
    }
    
    private static void MixedRecordAndPlayback()
    {
        SetOrCreateEngine(Capability.Mixed);
        
        // Create MicrophoneDataProvider and SoundPlayer
        var microphoneDataProvider = new MicrophoneDataProvider();
        var soundPlayer = new SoundPlayer(microphoneDataProvider);
        
        // Add noise suppression and AEC modifiers
        var apmModifier = new WebRtcApmModifier(nsEnabled: true, nsLevel: NoiseSuppressionLevel.VeryHigh);
        soundPlayer.AddModifier(apmModifier);

        // Add sound player to the master mixer
        Mixer.Master.AddComponent(soundPlayer);
        
        // Start capturing audio from the microphone and play it
        microphoneDataProvider.StartCapture();
        soundPlayer.Play();
        
        Console.WriteLine("Capturing and playing audio from the microphone with noise suppression.");
        Console.WriteLine("S' to toggle noise suppression, 'D' to toggle noise suppression level, Press any other key to stop.");

        var numberOfLevels = Enum.GetValues(typeof(NoiseSuppressionLevel)).Length;
        while (true)
        {
            if (Console.ReadKey(true).Key == ConsoleKey.S)
            {
                apmModifier.NoiseSuppression.Enabled = !apmModifier.NoiseSuppression.Enabled;
                Console.WriteLine($"Noise suppression enabled: {apmModifier.NoiseSuppression.Enabled}");
            }
            else if (Console.ReadKey(true).Key == ConsoleKey.D)
            {
                var currentIntValue = (int)apmModifier.NoiseSuppression.Level;

                // Calculate the index of the next value, wrapping around using modulo (%)
                var nextIntValue = (currentIntValue + 1) % numberOfLevels;

                // Convert the next index back to the enum type
                var nextLevel = (NoiseSuppressionLevel)nextIntValue;

                // Update the level
                apmModifier.NoiseSuppression.Level = nextLevel;
                
                Console.WriteLine($"Noise suppression level: {apmModifier.NoiseSuppression.Level}");
            }
            else
                break;
        }
        
        // Stop capturing and playing
        microphoneDataProvider.StopCapture();
        soundPlayer.Stop();
        Mixer.Master.RemoveComponent(soundPlayer);
        microphoneDataProvider.Dispose();
    }

    private static void CleanAudioFileFromNoise()
    {
        Console.WriteLine("Cleaning audio file from noise using NoiseSuppressor, Make sure to replace Sample Rate, Num of Channels, Suppression Level, Use Multichannel Processing with your actual values.");
        
        Console.Write("Enter noisy speech file path: ");
        var filePath = Console.ReadLine()?.Replace("\"", "") ?? string.Empty;

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return;
        }

        Console.WriteLine();
        
        // Create AssetDataProvider and NoiseSuppressor
        var dataProvider = new StreamDataProvider(new FileStream(filePath, FileMode.Open, FileAccess.Read));
        var noiseSuppressor = new NoiseSuppressor(
            dataProvider: dataProvider,
            sampleRate: 48000,
            numChannels: 1,
            suppressionLevel: NoiseSuppressionLevel.VeryHigh,
            useMultichannelProcessing: false
        );
        var stream = new FileStream(CleanedFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096);
        var encoder = AudioEngine.Instance.CreateEncoder(stream, EncodingFormat.Wav, SampleFormat.F32, 1, 48000);
        
        // Process the noisy speech file and save the cleaned audio
        Console.WriteLine("Processing noisy speech file...");
        
        var cleanData = noiseSuppressor.ProcessAll();
        encoder.Encode(cleanData.AsSpan());
        encoder.Dispose();
        stream.Dispose();
        
        Console.WriteLine($"Noise suppression applied. Cleaned audio file saved as 'cleaned-audio.wav' at {CleanedFilePath}, Press any key to exit.");
        Console.ReadLine();
        
        // Dispose noise suppressor and encoder
        noiseSuppressor.Dispose();
        dataProvider.Dispose();
    }
}