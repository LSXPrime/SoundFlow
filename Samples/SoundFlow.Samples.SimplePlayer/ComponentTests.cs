using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Modifiers;
using SoundFlow.Providers;
using SoundFlow.Structs;
using SoundFlow.Visualization;

namespace SoundFlow.Samples.SimplePlayer;

internal static class ComponentTests
{
    private static readonly AudioEngine Engine = new MiniAudioEngine();
    private static readonly AudioFormat Format = AudioFormat.DvdHq;

    public static void Run()
    {
        try
        {
            Console.WriteLine("SoundFlow Component and Modifier Examples");
            Console.WriteLine($"Using Audio Backend: {Engine.GetType().Name}");

            // Component Examples:
            Console.WriteLine("\n--- Component Examples ---");
            TestOscillator();
            TestLowFrequencyOscillator();
            TestEnvelopeGenerator();
            TestFilter();
            TestMixer();
            TestSoundPlayer();
            TestSurroundPlayer();
            TestRecorder(); // Note: Requires microphone
            TestVoiceActivityDetector(); // Note: Requires microphone
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
            TestParametricEqualizerModifier();
            TestTrebleBoosterModifier();

            Console.WriteLine("\nExamples Finished. Press any key to exit.");
            Console.ReadKey();
        }
        finally
        {
            Engine.Dispose();
        }
    }

    #region Component Tests

    private static void TestOscillator()
    {
        Console.WriteLine("\n- Testing Oscillator Component -");
        using var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        PlayComponentForDuration(oscillator, 3);
    }

    private static void TestEnvelopeGenerator()
    {
        Console.WriteLine("\n- Testing EnvelopeGenerator Component -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var envelope = new EnvelopeGenerator(Engine, Format)
        {
            AttackTime = 0.1f,
            DecayTime = 0.2f,
            SustainLevel = 0.7f,
            ReleaseTime = 1.0f
        };
        
        envelope.ConnectInput(oscillator);

        PlayComponentForDuration(envelope, 5, () =>
        {
            Console.WriteLine("Triggering envelope ON...");
            envelope.TriggerOn();
            Thread.Sleep(2000);
            Console.WriteLine("Triggering envelope OFF...");
            envelope.TriggerOff();
            Thread.Sleep(3000); // Wait for release to complete
        });
    }

    private static void TestLowFrequencyOscillator()
    {
        Console.WriteLine("\n- Testing LowFrequencyOscillator Component -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var lfo = new LowFrequencyOscillator(Engine, Format)
        {
            Rate = 2f, // 2 Hz
            Depth = 0.8f,
            Type = LowFrequencyOscillator.WaveformType.Sine,
            OnOutputChanged = value => { if (value > 0) oscillator.Volume = value; }
        };

        oscillator.ConnectInput(lfo);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestFilter()
    {
        Console.WriteLine("\n- Testing Filter Component -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var filter = new Filter(Engine, Format)
        {
            Type = Filter.FilterType.LowPass, 
            CutoffFrequency = 1000f, 
            Resonance = 0.8f
        };
        
        filter.ConnectInput(oscillator);
        
        PlayComponentForDuration(filter, 5);
    }

    private static void TestMixer()
    {
        Console.WriteLine("\n- Testing Mixer Component -");
        var mixer = new Mixer(Engine, Format);
        var osc1 = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.25f, Type = Oscillator.WaveformType.Sine };
        var osc2 = new Oscillator(Engine, Format) { Frequency = 660f, Amplitude = 0.25f, Type = Oscillator.WaveformType.Square };
        
        mixer.AddComponent(osc1);
        mixer.AddComponent(osc2);
        PlayComponentForDuration(mixer, 5);
    }

    private static void TestSoundPlayer()
    {
        Console.WriteLine("\n- Testing SoundPlayer Component -");
        const string filePath = "test_audio.mp3";
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Please ensure '{filePath}' is in the example directory. Skipping test.");
            return;
        }

        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var dataProvider = new StreamDataProvider(Engine, Format, fileStream);
        var soundPlayer = new SoundPlayer(Engine, Format, dataProvider);
        
        soundPlayer.Play();
        PlayComponentForDuration(soundPlayer, 5);
    }

    private static void TestSurroundPlayer()
    {
        Console.WriteLine("\n- Testing SurroundPlayer Component -");
        var filePath = "test_audio.mp3";
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Please ensure '{filePath}' is in the example directory. Skipping test.");
            return;
        }
        
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var dataProvider = new StreamDataProvider(Engine, Format, fileStream);
        var surroundPlayer = new SurroundPlayer(Engine, Format, dataProvider)
        {
            SpeakerConfig = SurroundPlayer.SpeakerConfiguration.Surround51,
            Panning = SurroundPlayer.PanningMethod.Vbap
        };
        
        surroundPlayer.Play();
        PlayComponentForDuration(surroundPlayer, 5);
    }
    
    private static void TestRecorder()
    {
        Console.WriteLine("\n- Testing Recorder Component -");
        Engine.UpdateDevicesInfo();
        DeviceInfo? deviceInfo = Engine.CaptureDevices.FirstOrDefault(d => d.IsDefault);
        if (deviceInfo == null)
        {
            Console.WriteLine("No capture device found. Skipping test.");
            return;
        }

        const string filePath = "output_recording.wav";
        Console.WriteLine($"Recording for 5 seconds to '{filePath}'...");
        
        using var captureDevice = Engine.InitializeCaptureDevice(deviceInfo.Value, Format);
        captureDevice.Start();

        var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        var recorder = new Recorder(captureDevice, stream);
        recorder.StartRecording();
        Thread.Sleep(5000);
        recorder.StopRecording();

        captureDevice.Stop();
        Console.WriteLine("Recording stopped and saved.");
    }

    private static void TestVoiceActivityDetector()
    {
        Console.WriteLine("\n- Testing VoiceActivityDetector Component -");
        Engine.UpdateDevicesInfo();
        DeviceInfo? captureDeviceInfo = Engine.CaptureDevices.FirstOrDefault(d => d.IsDefault);
        if (!captureDeviceInfo.HasValue)
        {
            Console.WriteLine("No capture device found. Skipping test.");
            return;
        }
        DeviceInfo? playbackDeviceInfo = Engine.PlaybackDevices.FirstOrDefault(d => d.IsDefault);
        if (!playbackDeviceInfo.HasValue)
        {
            Console.WriteLine("No playback device found. Skipping test.");
            return;
        }

        using var captureDevice = Engine.InitializeCaptureDevice(captureDeviceInfo.Value, Format);
        using var playbackDevice = Engine.InitializePlaybackDevice(playbackDeviceInfo.Value, Format);
        using var microphoneProvider = new MicrophoneDataProvider(captureDevice);
        using var soundPlayer = new SoundPlayer(Engine, Format, microphoneProvider);
        
        var vad = new VoiceActivityDetector(Format);
        vad.SpeechDetected += isSpeech => Console.WriteLine($"Voice Activity Detected: {isSpeech}");
        soundPlayer.AddAnalyzer(vad);

        playbackDevice.MasterMixer.AddComponent(soundPlayer);
        
        captureDevice.Start();
        playbackDevice.Start();
        microphoneProvider.StartCapture();
        soundPlayer.Play();

        Console.WriteLine("Speak into the microphone for 10 seconds to test VAD (passthrough is active)...");
        Thread.Sleep(10000);

        microphoneProvider.StopCapture();
        soundPlayer.RemoveAnalyzer(vad);
        soundPlayer.Stop();
        
        playbackDevice.Stop();
        captureDevice.Stop();
    }

    private static void TestLevelMeterAnalyzer()
    {
        Console.WriteLine("\n- Testing LevelMeterAnalyzer Component -");
        var levelMeter = new LevelMeterAnalyzer(Format);
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        oscillator.AddAnalyzer(levelMeter);

        PlayComponentForDuration(oscillator, 5, () =>
        {
            for (var i = 0; i < 5; i++)
            {
                Console.WriteLine($"Level Meter - RMS: {levelMeter.Rms:F3}, Peak: {levelMeter.Peak:F3}");
                Thread.Sleep(1000);
            }
        });
        oscillator.RemoveAnalyzer(levelMeter);
    }

    private static void TestSpectrumAnalyzer()
    {
        Console.WriteLine("\n- Testing SpectrumAnalyzer Component -");
        var spectrumAnalyzer = new SpectrumAnalyzer(Format,1024);
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sawtooth };
        oscillator.AddAnalyzer(spectrumAnalyzer);

        PlayComponentForDuration(oscillator, 5, () =>
        {
            for (var i = 0; i < 5; i++)
            {
                var spectrumData = spectrumAnalyzer.SpectrumData.ToArray();
                if (spectrumData.Length > 0)
                    Console.WriteLine($"Spectrum (first 10 bins): {string.Join(", ", spectrumData[..10].Select(s => s.ToString("F2")))}...");
                Thread.Sleep(1000);
            }
        });
        oscillator.RemoveAnalyzer(spectrumAnalyzer);
    }

    #endregion

    #region Modifier Tests

    private static void TestAlgorithmicReverbModifier()
    {
        Console.WriteLine("\n- Testing AlgorithmicReverbModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var reverb = new AlgorithmicReverbModifier(Format) { Wet = 0.5f, RoomSize = 0.8f };
        oscillator.AddModifier(reverb);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestBassBoosterModifier()
    {
        Console.WriteLine("\n- Testing BassBoosterModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 200f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var bassBooster = new BassBoosterModifier(Format) { Cutoff = 200f, BoostGain = 9f };
        oscillator.AddModifier(bassBooster);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestChorusModifier()
    {
        Console.WriteLine("\n- Testing ChorusModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var chorus = new ChorusModifier(Format) { DepthMs = 3f, RateHz = 1.0f, WetDryMix = 0.7f };
        oscillator.AddModifier(chorus);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestCompressorModifier()
    {
        Console.WriteLine("\n- Testing CompressorModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.8f, Type = Oscillator.WaveformType.Square };
        var compressor = new CompressorModifier(Format, -12f, 4f, 10f, 100f, 6f);
        oscillator.AddModifier(compressor);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestDelayModifier()
    {
        Console.WriteLine("\n- Testing DelayModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var delay = new DelayModifier(Format, (int)(Format.SampleRate * 0.5), 0.4f, 0.5f); // 0.5-second delay
        oscillator.AddModifier(delay);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestFrequencyBandModifier()
    {
        Console.WriteLine("\n- Testing FrequencyBandModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var bandPass = new FrequencyBandModifier(Format, 200f, 1000f);
        oscillator.AddModifier(bandPass);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestHighPassFilterModifier()
    {
        Console.WriteLine("\n- Testing HighPassFilter Modifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 100f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var highPass = new HighPassModifier(Format, 300f);
        oscillator.AddModifier(highPass);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestLowPassModifier()
    {
        Console.WriteLine("\n- Testing LowPassModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 880f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var lowPass = new LowPassModifier(Format, 500f);
        oscillator.AddModifier(lowPass);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestMultiChannelChorusModifier()
    {
        Console.WriteLine("\n- Testing MultiChannelChorusModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Sine };
        var multiChorus = new MultiChannelChorusModifier(Format,
            wetMix: 0.6f, maxDelay: (int)(Format.SampleRate * 0.05),
            [(depth: 2f, rate: 0.8f, feedback: 0.6f), (depth: 2.5f, rate: 1.1f, feedback: 0.65f)]);
        oscillator.AddModifier(multiChorus);
        PlayComponentForDuration(oscillator, 5);
    }
    
    private static void TestParametricEqualizerModifier()
    {
        Console.WriteLine("\n- Testing ParametricEqualizerModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 440f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var eq = new ParametricEqualizer(Format);
        eq.AddBands(
        [
            new EqualizerBand(FilterType.LowShelf, 100f, 6f, 0.7f),
            new EqualizerBand(FilterType.Peaking, 500f, -3f, 1.0f),
            new EqualizerBand(FilterType.HighShelf, 5000f, 3f, 0.7f)
        ]);
        oscillator.AddModifier(eq);
        PlayComponentForDuration(oscillator, 5);
    }

    private static void TestTrebleBoosterModifier()
    {
        Console.WriteLine("\n- Testing TrebleBoosterModifier -");
        var oscillator = new Oscillator(Engine, Format) { Frequency = 1000f, Amplitude = 0.5f, Type = Oscillator.WaveformType.Square };
        var trebleBooster = new TrebleBoosterModifier(Format) { Cutoff = 4000f, BoostGain = 9f };
        oscillator.AddModifier(trebleBooster);
        PlayComponentForDuration(oscillator, 5);
    }

    #endregion

    #region Helper Methods

    private static void PlayComponentForDuration(SoundComponent component, int durationSeconds, Action? playbackAction = null)
    {
        Engine.UpdateDevicesInfo();
        DeviceInfo? deviceInfo = Engine.PlaybackDevices.FirstOrDefault(d => d.IsDefault);
        if (deviceInfo == null)
        {
            Console.WriteLine("No playback device found. Skipping test.");
            return;
        }

        var playbackDevice = Engine.InitializePlaybackDevice(deviceInfo.Value, Format);
        playbackDevice.MasterMixer.AddComponent(component);
        playbackDevice.Start();

        Console.WriteLine($"Playing for {durationSeconds} seconds...");
        if (playbackAction != null)
        {
            playbackAction.Invoke();
        }
        else
        {
            Thread.Sleep(durationSeconds * 1000);
        }
        
        playbackDevice.Stop();
        playbackDevice.MasterMixer.RemoveComponent(component);
    }

    #endregion
}