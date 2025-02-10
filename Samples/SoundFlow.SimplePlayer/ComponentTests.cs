using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Modifiers;
using SoundFlow.Providers;
using SoundFlow.Visualization;

namespace SoundFlow.SimplePlayer;

internal static class ComponentTests
{
    private static AudioEngine _audioEngine = AudioEngine.Instance;

    public static void Run()
    {
        Console.WriteLine("SoundFlow Component and Modifier Examples");
        // Backend Initialization
        Console.WriteLine($"Using Audio Backend: {_audioEngine.GetType().Name}");

        // Component Examples:
        Console.WriteLine("\n--- Component Examples ---");

        TestOscillator();
        TestLowFrequencyOscillator();
        TestEnvelopeGenerator();
        TestFilter();
        TestMixer();

        TestSoundPlayer();
        TestSurroundPlayer();
        TestRecorder(); // Note: Requires user interaction
        TestVoiceActivityDetector(); // Note: Requires user interaction
        TestLevelMeterAnalyzer();
        TestSpectrumAnalyzer();

        // Modifier Examples:
        Console.WriteLine("\n--- Modifier Examples ---");
        TestAlgorithmicReverbModifier();
        TestBassBoosterModifier();
        TestChorusModifier();
        TestCompressorModifier();
        TestDelayModifier();
        TestFrequencyBandModifier();
        TestHighPassFilterModifier();
        TestLowPassModifier();
        TestMultiChannelChorusModifier();
        TestNoiseReductionModifier(); // Note: Might require longer audio input for effective noise estimation - Not Working
        TestParametricEqualizerModifier();
        TestTrebleBoosterModifier();


        Console.WriteLine("\nExamples Finished. Press any key to exit.");
        Console.ReadKey();
    }

    #region Component Tests

    private static void TestOscillator()
    {
        Console.WriteLine("\n- Testing Oscillator Component -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestEnvelopeGenerator()
    {
        Console.WriteLine("\n- Testing EnvelopeGenerator Component -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var envelope = new EnvelopeGenerator();
        envelope.TriggerOn();
        oscillator.ConnectInput(envelope);
        PlayComponentForDuration(oscillator, 5);
        envelope.TriggerOff(); // Trigger release after some time
        PlayComponentForDuration(oscillator, 2); // Let release complete
    }

    private static void TestLowFrequencyOscillator()
    {
        Console.WriteLine("\n- Testing LowFrequencyOscillator Component -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine, };
        var lfo = new LowFrequencyOscillator
        {
            Rate = 2f, Depth = 0.8f, Type = LowFrequencyOscillator.WaveformType.Sine,
            OnOutputChanged = value =>
            {
                if (float.IsPositive(value))
                    oscillator.Volume = value;
            }
        };
        oscillator.ConnectInput(lfo);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestFilter()
    {
        Console.WriteLine("\n- Testing Filter Component -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var filter = new Filter { Type = Filter.FilterType.LowPass, CutoffFrequency = 1000f, Resonance = 0.8f };
        filter.ConnectInput(oscillator);
        PlayComponentForDuration(filter, 5);
    }

    private static void TestMixer()
    {
        Console.WriteLine("\n- Testing Mixer Component -");
        var mixer = new Mixer();
        var osc1 = new Oscillator { Frequency = 440f, Amplitude = 0.25f, Type = Oscillator.WaveformType.Sine };
        var osc2 = new Oscillator { Frequency = 660f, Amplitude = 0.25f, Type = Oscillator.WaveformType.Square };
        mixer.AddComponent(osc1);
        mixer.AddComponent(osc2);
        PlayComponentForDuration(mixer, 5);
    }

    private static void TestSoundPlayer()
    {
        Console.WriteLine("\n- Testing SoundPlayer Component -");
        Console.WriteLine("Please ensure you have 'test_audio.mp3' in the example project directory.");

        using var fileStream = File.OpenRead("test_audio.mp3");
        var dataProvider = new StreamDataProvider(fileStream);
        var soundPlayer = new SoundPlayer(dataProvider);
        soundPlayer.Play();
        PlayComponentForDuration(soundPlayer, 5);
        soundPlayer.Stop();
    }

    private static void TestSurroundPlayer()
    {
        Console.WriteLine("\n- Testing SurroundPlayer Component -");
        Console.WriteLine("Please ensure you have 'test_audio.mp3' in the example project directory.");

        using var fileStream = File.OpenRead("test_audio.mp3");
        var dataProvider = new StreamDataProvider(fileStream);
        var surroundPlayer = new SurroundPlayer(dataProvider)
        {
            SpeakerConfig = SurroundPlayer.SpeakerConfiguration.Surround51, // Example 5.1 config
            Panning = SurroundPlayer.PanningMethod.Vbap // Example panning method
        };
        surroundPlayer.Play();
        PlayComponentForDuration(surroundPlayer, 5);
        surroundPlayer.Stop();
    }


    private static void TestRecorder()
    {
        Console.WriteLine("\n- Testing Recorder Component -");
        Console.WriteLine("Recording for 5 seconds to 'output_recording.wav'...");

        // Reinitialize audio engine for recording
        _audioEngine.Dispose();
        _audioEngine = new MiniAudioEngine(48000, Capability.Record);

        var recorder =
            new Recorder("output_recording.wav");
        recorder.StartRecording();
        Thread.Sleep(5000); // Record for 5 seconds
        recorder.StopRecording();
        Console.WriteLine("Recording stopped and saved to 'output_recording.wav'.");
    }


    private static void TestVoiceActivityDetector()
    {
        Console.WriteLine("\n- Testing VoiceActivityDetector Component -");
        var vad = new VoiceActivityDetector();
        vad.SpeechDetected += isSpeech => { Console.WriteLine($"Voice Activity Detected: {isSpeech}"); };

        var microphoneProvider = new MicrophoneDataProvider();
        var soundPlayer = new SoundPlayer(microphoneProvider); // Play microphone input
        soundPlayer.ConnectInput(vad); // VAD connected to microphone input
        microphoneProvider.StartCapture();
        soundPlayer.Play();

        Console.WriteLine("Speak into the microphone for 10 seconds to test VAD...");
        Thread.Sleep(10000);

        microphoneProvider.StopCapture();
        try
        {
            soundPlayer.Stop();
        }
        catch (Exception)
        {
            // Ignore as it will throw exception if soundPlayer since it's seeking to 0 on stop but MicrophoneDataProvider doesn't support seeking
        }

        soundPlayer.DisconnectInput(vad);
        microphoneProvider.Dispose();

        // Reinitialize audio engine for playback
        _audioEngine.Dispose();
        _audioEngine = new MiniAudioEngine(48000, Capability.Playback);
    }

    private static void TestLevelMeterAnalyzer()
    {
        Console.WriteLine("\n- Testing LevelMeterAnalyzer Component -");
        var levelMeter = new LevelMeterAnalyzer();
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        oscillator.AddAnalyzer(levelMeter);

        PlayComponentForDuration(oscillator, 10, () =>
        {
            for (var i = 0; i < 10; i++) // Monitor for 10 seconds
            {
                Console.WriteLine($"Level Meter - RMS: {levelMeter.Rms:F3}, Peak: {levelMeter.Peak:F3}");
                Thread.Sleep(1000);
            }
        });
    }

    private static void TestSpectrumAnalyzer()
    {
        Console.WriteLine("\n- Testing SpectrumAnalyzer Component -");
        var spectrumAnalyzer = new SpectrumAnalyzer(1024);
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sawtooth };
        oscillator.AddAnalyzer(spectrumAnalyzer);
        Mixer.Master.AddComponent(oscillator);

        PlayComponentForDuration(oscillator, 5, () =>
        {
            for (var i = 0; i < 5; i++)
            {
                var spectrumData = spectrumAnalyzer.SpectrumData;
                if (spectrumData.Length > 0)
                    Console.WriteLine(
                        $"Spectrum Data (First 10 bins) - {i}: {string.Join(", ", spectrumData[..Math.Min(10, spectrumData.Length)].ToArray().Select(s => s.ToString("F2")))}...");

                Thread.Sleep(1000);
            }
        });
    }

    #endregion

    #region Modifier Tests

    private static void TestAlgorithmicReverbModifier()
    {
        Console.WriteLine("\n- Testing AlgorithmicReverbModifier -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var reverb = new AlgorithmicReverbModifier { Wet = 0.5f, RoomSize = 0.8f };
        oscillator.AddModifier(reverb);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestBassBoosterModifier()
    {
        Console.WriteLine("\n- Testing BassBoosterModifier -");
        var oscillator = new Oscillator { Frequency = 200f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var bassBooster = new BassBoosterModifier { Cutoff = 200f, BoostGain = 9f };
        oscillator.AddModifier(bassBooster);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestChorusModifier()
    {
        Console.WriteLine("\n- Testing ChorusModifier -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var chorus = new ChorusModifier { DepthMs = 3f, RateHz = 1.0f, WetDryMix = 0.7f };
        oscillator.AddModifier(chorus);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestCompressorModifier()
    {
        Console.WriteLine("\n- Testing CompressorModifier -");
        var oscillator = new Oscillator
        {
            Frequency = 440f, Amplitude = 0.8f, Type = Oscillator.WaveformType.Square
        }; // Louder signal for compression
        var compressor = new CompressorModifier(-12f, 4f, 10f, 100f, makeupGainDb: 6f);
        oscillator.AddModifier(compressor);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestDelayModifier()
    {
        Console.WriteLine("\n- Testing DelayModifier -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var delay = new DelayModifier(44100 / 2, 0.4f, 0.5f);
        oscillator.AddModifier(delay);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestFrequencyBandModifier()
    {
        Console.WriteLine("\n- Testing FrequencyBandModifier -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var bandPass = new FrequencyBandModifier(200f, 1000f); // Pass frequencies between 200Hz and 1kHz
        oscillator.AddModifier(bandPass);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestHighPassFilterModifier()
    {
        Console.WriteLine("\n- Testing HighPassFilter Modifier -");
        var oscillator = new Oscillator
            { Frequency = 100f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square }; // Low freq to be filtered
        var highPass = new HighPassFilter(300f);
        oscillator.AddModifier(highPass);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestLowPassModifier()
    {
        Console.WriteLine("\n- Testing LowPassModifier -");
        var oscillator = new Oscillator
            { Frequency = 880f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square }; // High freq to be filtered
        var lowPass = new LowPassModifier(500f);
        oscillator.AddModifier(lowPass);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestMultiChannelChorusModifier()
    {
        Console.WriteLine("\n- Testing MultiChannelChorusModifier -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var multiChorus = new MultiChannelChorusModifier(
            wetMix: 0.6f,
            maxDelay: 44100 / 20, // Example max delay
            channelParameters:
            // Example parameters for stereo (2-channel)
            [
                (depth: 2f, rate: 0.8f, feedback: 0.6f),
                (depth: 2.5f, rate: 1.1f, feedback: 0.65f)
            ]);
        oscillator.AddModifier(multiChorus);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestNoiseReductionModifier()
    {
        Console.WriteLine("\n- Testing NoiseReductionModifier -");
        
        var mixer = new Mixer();
        var sineOsc = new Oscillator
            { Frequency = 440f, Amplitude = 0.3f, Type = Oscillator.WaveformType.Sine }; // Signal
        var noiseOsc = new Oscillator
            { Frequency = 0f, Amplitude = 0.3f, Type = Oscillator.WaveformType.Noise }; // Noise
        mixer.AddComponent(sineOsc);
        mixer.AddComponent(noiseOsc);
        mixer.AddModifier(new NoiseReductionModifier()); // Apply noise reduction to the mixed signal
        PlayComponentForDuration(mixer, 25);
    }


    private static void TestParametricEqualizerModifier()
    {
        Console.WriteLine("\n- Testing ParametricEqualizerModifier -");
        var oscillator = new Oscillator { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var eq = new ParametricEqualizer();
        eq.AddBands(new[] // Example EQ bands
        {
            new EqualizerBand(FilterType.LowShelf, 100f, 6f, 0.7f), // Boost lows
            new EqualizerBand(FilterType.Peaking, 500f, -3f, 1.0f), // Cut mid
            new EqualizerBand(FilterType.HighShelf, 5000f, 3f, 0.7f) // Boost highs
        });
        oscillator.AddModifier(eq);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestTrebleBoosterModifier()
    {
        Console.WriteLine("\n- Testing TrebleBoosterModifier -");
        var oscillator = new Oscillator
            { Frequency = 1000f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square }; // Mid-high freq
        var trebleBooster = new TrebleBoosterModifier { Cutoff = 4000f, BoostGain = 9f };
        oscillator.AddModifier(trebleBooster);
        PlayComponentForDuration(oscillator, 5);
    }

    #endregion


    #region Helper Methods

    private static void PlayComponentForDuration(SoundComponent component, int durationSeconds,
        Action? playbackAction = null)
    {
        Mixer.Master.AddComponent(component);
        if (playbackAction != null)
            playbackAction.Invoke();
        else
            Thread.Sleep(durationSeconds * 1000);
        Mixer.Master.RemoveComponent(component);
    }

    #endregion
}