using System.Numerics;
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Experimental;
using SoundFlow.Interfaces;
using SoundFlow.Modifiers;
using SoundFlow.Providers;
using SoundFlow.Visualization;
using VoiceActivityDetector = SoundFlow.Components.VoiceActivityDetector;

namespace SoundFlow.Samples.SimplePlayer;

/// <summary>
/// Example program to play audio, record, and apply effects using SoundFlow.
/// </summary>
internal static class Program
{
    private static AudioEngine? _audioEngine;
    private static readonly string RecordedFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recorded.wav");
    
    private static void Main()
    {
        SetOrCreateEngine();

        while (true)
        {
            Console.WriteLine("\nChoose an option:");
            Console.WriteLine("1. Play audio from file");
            Console.WriteLine("2. Play audio from web stream");
            Console.WriteLine("3. Record and playback audio");
            Console.WriteLine("4. Play audio from microphone");
            Console.WriteLine("5. Play audio with visualization");
            Console.WriteLine("6. Play audio with noise reduction");
            Console.WriteLine("7. Play audio with equalizer");
            Console.WriteLine("8. Run component and modifier tests");
            Console.WriteLine("9. Run data provider tests");
            Console.WriteLine("Press any other key to exit.");

            var choice = Console.ReadKey().KeyChar;
            Console.WriteLine();

            switch (choice)
            {
                case '1':
                    PlayAudioFromFile();
                    break;
                case '2':
                    PlayAudioFromWeb();
                    break;
                case '3':
                    RecordAndPlaybackAudio();
                    break;
                case '4':
                    MixedRecordAndPlayback();
                    break;
                case '5':
                    PlayAudioVisualizer();
                    break;
                case '6':
                    PlayAudioWithNoiseReduction();
                    break;
                case '7':
                    PlayAudioWithEqualizer();
                    break;
                case '8':
                    ComponentTests.Run();
                    break;
                case '9':
                    DataProviderTests.Run();
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
        SampleFormat sampleFormat = SampleFormat.F32, int channels = 2)
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

    private static void PlayAudio(ISoundDataProvider dataProvider, bool isSurround = false,
        Action<ISoundPlayer>? configurePlayer = null, List<SoundModifier>? modifiers = null)
    {
        SetOrCreateEngine();
        SoundPlayerBase soundPlayer = isSurround ? new SurroundPlayer(dataProvider) : new SoundPlayer(dataProvider);

        if (modifiers != null)
        {
            foreach (var modifier in modifiers)
            {
                soundPlayer.AddModifier(modifier);
            }
        }

        Mixer.Master.AddComponent(soundPlayer);
        configurePlayer?.Invoke(soundPlayer);

        soundPlayer.Play();

        PlaybackControls(soundPlayer);

        Mixer.Master.RemoveComponent(soundPlayer);
    }

    private static void PlaybackControls(ISoundPlayer player)
    {
        var timer = new System.Timers.Timer(500) { AutoReset = true };
        timer.Elapsed += (_, _) =>
        {
            if (player.State != PlaybackState.Stopped)
            {
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write(
                    $"Time: {(int)player.Time / 60}:{player.Time % 60:00} / Duration: {(int)player.Duration / 60}:{player.Duration % 60:00}        ");
            }
            else
            {
                timer.Stop();
            }
        };
        timer.Start();

        Console.WriteLine(
            "\nPress 's' to seek, 'p' to pause/play, any other key to exit playback. '+' to increase speed, '-' to decrease speed, 'R' to reset speed to 1.0");


        while (player.State is PlaybackState.Playing or PlaybackState.Paused)
        {
            var keyInfo = Console.ReadKey(true);
            switch (keyInfo.Key)
            {
                case ConsoleKey.P:
                    if (player.State == PlaybackState.Playing)
                        player.Pause();
                    else
                        player.Play();
                    break;
                case ConsoleKey.S:
                    Console.WriteLine("Enter seek time in seconds (e.g., 5.0):");
                    if (float.TryParse(Console.ReadLine(), out var seekTime))
                        player.Seek(TimeSpan.FromSeconds(seekTime));
                    else
                        Console.WriteLine("Invalid seek time.");
                    break;
                case ConsoleKey.OemPlus:
                case ConsoleKey.Add:
                    {
                        player.PlaybackSpeed += 0.1f;
                        Console.WriteLine($"Speed increased to: {player.PlaybackSpeed:F2}");
                    }

                    break;
                case ConsoleKey.OemMinus:
                case ConsoleKey.Subtract:
                    if (player.PlaybackSpeed > 0.1f)
                    {
                        player.PlaybackSpeed -= 0.1f;
                        Console.WriteLine($"Speed decreased to: {player.PlaybackSpeed:F2}");
                    }

                    break;
                case ConsoleKey.R:
                    player.PlaybackSpeed = 1.0f;
                    Console.WriteLine($"Speed reset to: {player.PlaybackSpeed:F2}");
                    break;
                default:
                    player.Stop();
                    break;
            }
        }

        timer.Stop();
        timer.Dispose();
        Console.WriteLine("Playback stopped.");
    }

    private static void PlayAudioFromFile()
    {
        Console.Write("Enter file path: ");
        var filePath = Console.ReadLine()?.Replace("\"", "") ?? string.Empty;

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return;
        }

        Console.Write("Would you like to use surround sound? (y/n): ");
        var isSurround = Console.ReadKey().Key == ConsoleKey.Y;
        Console.WriteLine();

        PlayAudio(new StreamDataProvider(new FileStream(filePath, FileMode.Open, FileAccess.Read)), isSurround,
            player =>
            {
                if (isSurround && player is SurroundPlayer surroundPlayer)
                {
                    surroundPlayer.Panning = SurroundPlayer.PanningMethod.Vbap;
                    surroundPlayer.ListenerPosition = new Vector2(0.9f, 0.5f);
                    surroundPlayer.SpeakerConfig = SurroundPlayer.SpeakerConfiguration.Surround71;
                }
            }, []);
    }

    private static void PlayAudioFromWeb()
    {
        Console.Write("Enter web stream URL: ");
        var url = Console.ReadLine() ?? string.Empty;

        try
        {
            var networkDataProvider = new NetworkDataProvider(url);
            PlayAudio(networkDataProvider);
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"Error accessing web stream: {e.Message}");
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine($"Error initializing network stream: {e.Message}");
        }
    }

    private static void MixedRecordAndPlayback()
    {
        SetOrCreateEngine(Capability.Mixed);
        
        // Create MicrophoneDataProvider and SoundPlayer
        var microphoneDataProvider = new MicrophoneDataProvider();
        var soundPlayer = new SoundPlayer(microphoneDataProvider);

        // Add sound player to the master mixer
        Mixer.Master.AddComponent(soundPlayer);
        
        // Start capturing audio from the microphone and play it
        microphoneDataProvider.StartCapture();
        soundPlayer.Play();
        
        Console.WriteLine("Capturing and playing audio from the microphone. Press any key to stop.");
        Console.ReadKey();
        
        // Stop capturing and playing
        microphoneDataProvider.StopCapture();
        soundPlayer.Stop();
        Mixer.Master.RemoveComponent(soundPlayer);
        microphoneDataProvider.Dispose();
    }

    private static void RecordAndPlaybackAudio()
    {
        SetOrCreateEngine(Capability.Record);
        
        using var recorder = new Recorder(RecordedFilePath, SampleFormat.F32, EncodingFormat.Wav, 48000);

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

        Console.WriteLine("Recording finished. Press 'p' to playback or any other key to exit.");
        if (Console.ReadKey(true).Key != ConsoleKey.P)
            return;

        if (!File.Exists(RecordedFilePath))
        {
            Console.WriteLine("Recorded file not found.");
            return;
        }

        PlayAudio(new StreamDataProvider(new FileStream(RecordedFilePath, FileMode.Open, FileAccess.Read)));
    }

    private static void PlayAudioVisualizer()
    {
        Console.Write("Enter file path for visualization: ");
        var filePath = Console.ReadLine() ?? string.Empty;

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return;
        }

        SetOrCreateEngine();
        var dataProvider = new StreamDataProvider(new FileStream(filePath, FileMode.Open, FileAccess.Read));
        var soundPlayer = new SoundPlayer(dataProvider);
        var waveformVisualizer = new WaveformVisualizer();

        waveformVisualizer.VisualizationUpdated += (_, _) =>
            SaveWaveformAsText("waveform.txt", 80, 20, waveformVisualizer.Waveform);

        AudioEngine.OnAudioProcessed += ProcessOnAudioData;

        Mixer.Master.AddComponent(soundPlayer);
        soundPlayer.Play();

        PlaybackControls(soundPlayer);

        Mixer.Master.RemoveComponent(soundPlayer);
        AudioEngine.OnAudioProcessed -= ProcessOnAudioData;
        return;

        void ProcessOnAudioData(Span<float> samples, Capability _)
        {
            waveformVisualizer.ProcessOnAudioData(samples);
        }
    }

    private static void PlayAudioWithNoiseReduction()
    {
        Console.Write("Enter file path with noise: ");
        var noisyFilePath = Console.ReadLine() ?? string.Empty;

        if (!File.Exists(noisyFilePath))
        {
            Console.WriteLine("File not found.");
            return;
        }


        var noiseReductionModifier = new NoiseReductionModifier(
            fftSize: 2048,
            alpha: 3f,
            beta: 0.001f,
            gain: 1.2f,
            noiseFrames: 50
        );

        PlayAudio(new StreamDataProvider(new FileStream(noisyFilePath, FileMode.Open, FileAccess.Read)),
            modifiers: [noiseReductionModifier]);
    }

    private static void PlayAudioWithEqualizer()
    {
        Console.Write("Enter file path for equalizer: ");
        var filePath = Console.ReadLine() ?? string.Empty;

        if (!File.Exists(filePath))
        {
            Console.WriteLine("File not found.");
            return;
        }

        SetOrCreateEngine(sampleRate: 44100, sampleFormat: SampleFormat.F32);
        var parametricEqualizer = new ParametricEqualizer();

        Console.WriteLine("\nChoose an equalizer preset:");
        var presets = EqualizerPresets.GetAllPresets();
        var presetNames = presets.Keys.ToList();
        for (var i = 0; i < presetNames.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {presetNames[i]}");
        }

        if (int.TryParse(Console.ReadLine(), out var presetChoice) && presetChoice > 0 &&
            presetChoice <= presetNames.Count)
        {
            parametricEqualizer.AddBands(presets[presetNames[presetChoice - 1]]);
            Console.WriteLine($"\nApplied preset: {presetNames[presetChoice - 1]}");
        }
        else
        {
            Console.WriteLine("\nNo valid preset selected. Using default.");
            parametricEqualizer.AddBands(presets["Default"]);
        }

        Console.WriteLine();

        PlayAudio(new StreamDataProvider(new FileStream(filePath, FileMode.Open, FileAccess.Read)),
            modifiers: [parametricEqualizer]);
    }

    private static void SaveWaveformAsText(string filePath, int width, int height, List<float> waveform)
    {
        if (waveform.Count == 0)
        {
            return;
        }

        var yScale = height / 2.0f;
        var waveformChars = new char[height, width];
        for (var i = 0; i < height; i++)
        {
            for (var j = 0; j < width; j++)
            {
                waveformChars[i, j] = ' ';
            }
        }

        char[] charMap = [' ', '.', ':', '|', '#', '@'];
        var numChars = charMap.Length;

        for (var i = 0; i < width; i++)
        {
            var sampleIndex = (int)(i * (waveform.Count / (float)width));
            var sampleValue = waveform[Math.Clamp(sampleIndex, 0, waveform.Count - 1)];
            var charIndex = (int)((sampleValue + 1) / 2 * (numChars - 1));
            charIndex = Math.Clamp(charIndex, 0, numChars - 1);
            var y = (int)((1 - sampleValue) * yScale);
            y = Math.Clamp(y, 0, height - 1);
            waveformChars[y, i] = charMap[charIndex];
        }

        using StreamWriter writer = new(filePath);
        for (var i = 0; i < height; i++)
        {
            for (var j = 0; j < width; j++)
            {
                writer.Write(waveformChars[i, j]);
            }

            writer.WriteLine();
        }
    }
}