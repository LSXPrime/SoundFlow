using SoundFlow.Backends.MiniAudio;
using SoundFlow.Providers;
using SoundFlow.Structs;
using SoundFlow.Synthesis;
using SoundFlow.Synthesis.Banks;

Console.WriteLine("Standalone Sequencer MIDI Player Example");

// User Configuration
const string midiFilePath = "path/to/your/midi/file.mid";
const string soundFontPath = "path/to/your/soundfont/file.sf2";

if (!File.Exists(midiFilePath) || !File.Exists(soundFontPath))
{
    Console.WriteLine("Error: Please provide valid paths for the MIDI and SoundFont files.");
    return;
}

// 1. Standard engine and device setup
using var engine = new MiniAudioEngine();
var format = AudioFormat.Cd;
using var device = engine.InitializePlaybackDevice(engine.PlaybackDevices.FirstOrDefault(x => x.IsDefault), format);

// 2. Load MIDI data and an instrument bank
var midiDataProvider = new MidiDataProvider(File.OpenRead(midiFilePath));
using var instrumentBank = new SoundFontBank(soundFontPath, format);

Console.WriteLine($"Loaded MIDI file: {Path.GetFileName(midiFilePath)}, Duration: {midiDataProvider.Duration:mm\\:ss}");
Console.WriteLine($"Loaded SoundFont with {instrumentBank.AvailablePresets.Count} presets.");

// 3. Create the Synthesizer (the sound source)
var synthesizer = new Synthesizer(engine, format, instrumentBank);

// 4. Create the Sequencer (the MIDI event dispatcher)
var sequencer = new Sequencer(engine, format, midiDataProvider, synthesizer);

// 5. Build the audio graph

// The Synthesizer generates the audio.
device.MasterMixer.AddComponent(synthesizer);

// The Sequencer is also added so its GenerateAudio method is called for timing, even though it doesn't output audio itself.
device.MasterMixer.AddComponent(sequencer);

// 6. Start playback
device.Start();
sequencer.Play();

Console.WriteLine("\nPlayback started. Press any key to stop.");
Console.ReadKey();

// 7. Clean up
sequencer.Stop();
device.Stop();
synthesizer.Dispose();