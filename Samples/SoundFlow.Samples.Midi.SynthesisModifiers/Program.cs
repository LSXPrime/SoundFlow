using SoundFlow.Backends.MiniAudio;
using SoundFlow.Midi.Modifier;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;
using SoundFlow.Synthesis;
using SoundFlow.Synthesis.Banks;


Console.WriteLine("Initializing Audio Engine...");

// 1. Standard Audio Setup
using var engine = new MiniAudioEngine();
using var output = engine.InitializePlaybackDevice(null, AudioFormat.DvdHq);
output.Start();

// 2. Setup Synthesizer
var bank = new BasicInstrumentBank(output.Format);
var synth = new Synthesizer(engine, output.Format, bank);

// Connect Synth to Audio Output
output.MasterMixer.AddComponent(synth);

// 3. Create Modifiers

// Modifier 1: Arpeggiator (Generates rhythm)
var arp = new ArpeggiatorModifier
{
    Mode = ArpMode.Up,
    Octaves = 2,
    Rate = 0.125, // 1/32 notes (very fast)
    Gate = 0.5, // Short staccato notes
    IsEnabled = true
};

// Modifier 2: Transposer (Global pitch shift)
var transposer = new TransposeModifier(-12); // Drop everything down an octave

// Modifier 3: Harmonizer (Make chords out of the single arpeggiated notes)
var harmonizer = new HarmonizerModifier([0, 7]); // Power chords (Root + 5th)

// Add them to the chain: Arp -> Transpose -> Harmonize -> Sound
synth.AddMidiModifier(arp);
synth.AddMidiModifier(transposer);
synth.AddMidiModifier(harmonizer);

// Set Tempo
synth.Bpm = 130;

Console.WriteLine("--------------------------------------------------");
Console.WriteLine(" SOUNDFLOW INTEGRATED ARPEGGIATOR DEMO");
Console.WriteLine("--------------------------------------------------");
Console.WriteLine(" [Space] : Toggle Arp Mode");
Console.WriteLine(" [Up]    : Increase Tempo");
Console.WriteLine(" [Down]  : Decrease Tempo");
Console.WriteLine(" [Esc]   : Quit");
Console.WriteLine("--------------------------------------------------");

// 4. Input Phase
// Simply send a chord to the synthesizer.
// The Arpeggiator consumes these notes and starts its internal clock automatically.
Console.WriteLine(">> Sending C-Minor 7 Chord (C4, Eb4, G4, Bb4)...");

int[] chord = [60, 63, 67, 70];
foreach (var note in chord)
{
    // Send Note On
    synth.ProcessMidiMessage(new MidiMessage(0x90, (byte)note, 100));
}

// 5. UI Loop - The synth handles all playback autonomously
var running = true;
while (running)
{
    if (!Console.KeyAvailable)
    {
        Thread.Sleep(10);
        continue;
    }

    var key = Console.ReadKey(true).Key;
    switch (key)
    {
        case ConsoleKey.Spacebar:
            arp.Mode = arp.Mode switch
            {
                ArpMode.Up => ArpMode.Down,
                ArpMode.Down => ArpMode.UpDown,
                ArpMode.UpDown => ArpMode.Random,
                _ => ArpMode.Up
            };
            Console.WriteLine($"Mode: {arp.Mode}");
            break;

        case ConsoleKey.UpArrow:
            synth.Bpm += 10;
            Console.WriteLine($"BPM: {synth.Bpm}");
            break;

        case ConsoleKey.DownArrow:
            synth.Bpm = Math.Max(10, synth.Bpm - 10);
            Console.WriteLine($"BPM: {synth.Bpm}");
            break;

        case ConsoleKey.Escape:
            running = false;
            break;
    }
}

// 6. Cleanup
// Release notes before quitting (good practice)
foreach (var note in chord)
{
    synth.ProcessMidiMessage(new MidiMessage(0x80, (byte)note, 0));
}

Console.WriteLine("Exiting...");