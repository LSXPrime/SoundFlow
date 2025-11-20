using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Editing;
using SoundFlow.Editing.Mapping;
using SoundFlow.Interfaces;
using SoundFlow.Midi.PortMidi;
using SoundFlow.Structs;

namespace SoundFlow.Samples.Midi.PropertyMapping;

/// <summary>
/// This program demonstrates how to programmatically create a real-time MIDI mapping.
/// It links a physical knob on a MIDI controller (CC #21) to the 'DownsampleFactor'
/// parameter of a custom BitCrusher audio effect.
///
/// An sound player is playing a audio file, which is processed by the BitCrusher.
/// When the user turns the mapped knob, the bit-crushing effect is audible in real-time.
/// </summary>
public static class Program
{
    public static void Main()
    {
        // 1. Standard Engine & MIDI Backend Setup
        using var engine = new MiniAudioEngine();
        engine.UsePortMidi();
        engine.UpdateMidiDevicesInfo();
        var format = AudioFormat.DvdHq;

        // Initialize Audio Device
        engine.UpdateAudioDevicesInfo();
        var playbackDevice = engine.InitializePlaybackDevice(null, format);

        // 2. Create a Composition and Target Component
        var composition = new Composition(engine, format);
        var audioTrack = new Track("My Audio Track");
        composition.Editor.AddTrack(audioTrack);

        var bitCrusher = new BitCrusherModifier();
        audioTrack.Settings.AddModifier(bitCrusher);
        Console.WriteLine($"Created BitCrusher with ID: {bitCrusher.Id}");

        
        Console.Write("Enter the path to a audio file (.wav): ");
        // Trim quotes in case the user drag-and-drops a file path onto the console.
        var filePath = Console.ReadLine()?.Trim('"');

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Console.WriteLine("File not found or path was empty.");
            return;
        }
        
        composition.Editor.CreateSegmentFromFile(filePath, composition.Editor.CalculateTotalDuration());
        
        // 3. Define the Mapping
        var inputDeviceInfo = engine.MidiInputDevices.FirstOrDefault();
        if (inputDeviceInfo.Name == null)
        {
            Console.WriteLine("No MIDI input device found. Cannot create mapping.");
            return;
        }

        // a) Define the MIDI Source (e.g., CC #21 on our keyboard)
        var source = new MidiInputSource
        {
            DeviceName = inputDeviceInfo.Name,
            Channel = 0, // Omni: listen on all channels
            MessageType = MidiMappingSourceType.ControlChange,
            MessageParameter = 21 // The CC number to listen for
        };

        // b) Define the Target (the 'DownsampleFactor' property of our BitCrusher)
        var target = new MidiMappingTarget
        {
            TargetObjectId = bitCrusher.Id, // Link to our specific BitCrusher instance
            TargetType = MidiMappingTargetType.Property,
            TargetMemberName = "DownsampleFactor"
        };

        // c) Define the Transformer (map MIDI 0-127 to parameter 1.0-50.0)
        var transformer = new ValueTransformer
        {
            SourceMin = 0,
            SourceMax = 127,
            TargetMin = 0.0f, // These map to the normalized range (0.0 to 1.0)
            TargetMax = 1.0f,
            CurveType = MidiMappingCurveType.Linear // Linear for simplicity here
        };

        // 4. Create the MidiMapping Object
        var mapping = new MidiMapping(source, target, transformer);

        // 5. Add the mapping to the manager
        composition.MappingManager.AddMapping(mapping);
        Console.WriteLine($"Mapping created: CC #21 on '{inputDeviceInfo.Name}' -> BitCrusher.DownsampleFactor");

        // 6. To make it listen, we must add the device to the manager.
        // GetOrCreateInputNode initializes the device and makes the manager aware of it.
        var inputNode = engine.MidiManager.GetOrCreateInputNode(inputDeviceInfo);
        composition.MappingManager.AddInputDevice(inputNode.Device);
        inputNode.Device.OnMessageReceived += (message, _) =>
        {
            Console.WriteLine(
                $"[Received] Command: {message.Command}, " +
                $"Channel: {message.Channel}, " +
                $"Controller Number: {message.ControllerNumber}" +
                $"Controller Value: {message.ControllerValue}" +
                $"Note/CC: {message.Data1}, " +
                $"Value: {message.Data2}, " +
                $"Timestamp: {message.Timestamp}");
        };

        var soundPlayer = new SoundPlayer(engine, format, composition.Renderer);
        

        // Start Audio Playback
        playbackDevice.Start();
        soundPlayer.Play();

        Console.WriteLine("\nAudio is now playing through the BitCrusher.");
        Console.WriteLine("Mapping is live. Turn CC knob #21 on your controller to hear the effect.");
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();

        // Clean up resources
        playbackDevice.Stop();
        composition.Dispose();
        playbackDevice.Dispose();
    }
}

// Define an enum for one of our parameters.
public enum NoiseType { None, White, Pink }

/// <summary>
/// A custom SoundModifier that applies a bit-crushing effect to audio.
/// It inherits from SoundModifier, which already implements IMidiMappable.
/// </summary>
public class BitCrusherModifier : SoundModifier
{
    private float _lastSample;
    private int _downsampleCounter;
    private readonly Random _random = new();

    public override string Name { get; set; } = "BitCrusher";

    // Mappable Parameters are decorated with [ControllableParameter]
    // This attribute provides metadata for the mapping system and UI controls.

    [ControllableParameter("Bit Depth", 1.0, 16.0)]
    public float BitDepth { get; set; } = 8.0f;

    [ControllableParameter("Downsampling", 1.0, 50.0, MappingScale.Logarithmic)]
    public float DownsampleFactor { get; set; } = 1.0f;

    [ControllableParameter("Add Noise", 0, 1)] // Min/Max for bool (0=false, 1=true)
    public bool AddNoise { get; set; } = false;

    [ControllableParameter("Noise Type", 0, 2)] // Min/Max for enum values
    public NoiseType Noise { get; set; } = NoiseType.White;

    [ControllableParameter("Mix", 0.0, 1.0)]
    public float WetDryMix { get; set; } = 1.0f;

    // Processing Logic

    public override float ProcessSample(float sample, int channel)
    {
        // 1. Downsampling (Sample Rate Reduction)
        float processedSample;
        if (_downsampleCounter >= DownsampleFactor)
        {
            _downsampleCounter = 0;
            _lastSample = sample;
            processedSample = sample;
        }
        else
        {
            processedSample = _lastSample;
            _downsampleCounter++;
        }

        // 2. Quantization (Bit Depth Reduction)
        var steps = MathF.Pow(2, BitDepth);
        processedSample = MathF.Round(processedSample * (steps - 1)) / (steps - 1);

        // 3. Add Noise (Optional)
        if (AddNoise)
        {
            float noise = 0;
            if (Noise == NoiseType.White)
            {
                noise = ((float)_random.NextDouble() * 2.0f - 1.0f) / steps;
            }
            // A more complex Pink noise implementation would go here.
            processedSample += noise;
        }

        // 4. Mix original and processed signal
        return sample * (1.0f - WetDryMix) + processedSample * WetDryMix;
    }

    public override void Process(Span<float> buffer, int channels)
    {
        if (!Enabled) return;

        for (var i = 0; i < buffer.Length; i++)
        {
            // We assume mono processing for simplicity, using channel 0's state
            buffer[i] = ProcessSample(buffer[i], 0);
        }
    }
}