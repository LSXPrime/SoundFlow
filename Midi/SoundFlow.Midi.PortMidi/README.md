<div align="center">
    <img src="https://raw.githubusercontent.com/LSXPrime/SoundFlow/refs/heads/master/logo.png" alt="Project Logo" width="256" height="256">

# SoundFlow - MIDI Backend Extension (PortMidi)

**Cross-Platform MIDI I/O and Synchronization for SoundFlow using PortMidi**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![NuGet](https://img.shields.io/nuget/v/SoundFlow.Midi.PortMidi.svg)](https://www.nuget.org/packages/SoundFlow.Midi.PortMidi)
[![SoundFlow Main Repository](https://img.shields.io/badge/SoundFlow%20Core-Repo-blue)](https://github.com/LSXPrime/SoundFlow)

</div>

## Introduction

`SoundFlow.Midi.PortMidi` is an official backend extension for the [SoundFlow (.NET) audio engine](https://github.com/LSXPrime/SoundFlow). It integrates the widely-used, cross-platform **[PortMidi](https://github.com/PortMidi/portmidi)** library to provide robust and low-latency MIDI input/output capabilities.

This package enables your SoundFlow applications to communicate with MIDI hardware (keyboards, synthesizers, controllers) and other MIDI-enabled software. Beyond basic I/O, it includes a sophisticated synchronization manager, allowing SoundFlow to act as a master clock source or sync as a slave to external devices.

## Features

This extension provides a complete MIDI solution for SoundFlow, including:

*   **Cross-Platform MIDI I/O:** Seamlessly send and receive MIDI messages on Windows, macOS, and Linux, thanks to the PortMidi native library.
*   **MIDI Device Management:** Enumerate, open, and manage multiple independent MIDI input and output devices.
*   **Advanced MIDI Synchronization:**
    *   **Master Mode:** Act as a master clock source, sending sample-accurate MIDI Clock, Start, Stop, and Continue messages to synchronize external hardware or software.
    *   **Slave Mode:** Synchronize the SoundFlow transport to an external source, with support for:
        *   **MIDI Clock:** Includes automatic BPM detection.
        *   **MIDI Time Code (MTC):** For frame-accurate sync with video or other professional equipment.
*   **Robust Message Handling:** Full support for all standard MIDI message types, including:
    *   Channel Messages (Note On/Off, CC, Pitch Bend, etc.).
    *   System Exclusive (SysEx) for device-specific communication.
    *   System Real-Time and System Common messages for transport control.

## Getting Started

### Installation

This package requires the core SoundFlow library. Install it via NuGet:

**NuGet Package Manager:**

```bash
Install-Package SoundFlow.Midi.PortMidi
```

**.NET CLI:**

```bash
dotnet add package SoundFlow.Midi.PortMidi
```

### Usage

To enable MIDI functionality, you must configure the audio engine to use the `PortMidi` backend.

#### Example 1: Basic MIDI I/O (MIDI Thru)

This example shows how to list devices and create a simple "MIDI Thru" route from an input to an output.

```csharp
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Midi.PortMidi; // Import the extension namespace
using SoundFlow.Midi.Routing;

// 1. Initialize an audio engine (PortMidi can coexist with any audio backend).
using var engine = new MiniAudioEngine();

// 2. Enable the PortMidi backend. This returns the backend instance for configuration.
var midiBackend = engine.UsePortMidi();

// 3. Refresh and list available MIDI devices.
engine.UpdateMidiDevicesInfo();

Console.WriteLine("--- MIDI Inputs ---");
foreach (var input in engine.MidiInputDevices)
{
    Console.WriteLine($"ID: {input.Id}, Name: {input.Name}");
}

Console.WriteLine("\n--- MIDI Outputs ---");
foreach (var output in engine.MidiOutputDevices)
{
    Console.WriteLine($"ID: {output.Id}, Name: {output.Name}");
}

// 4. Select the first available input and output devices.
var firstInput = engine.MidiInputDevices.FirstOrDefault();
var firstOutput = engine.MidiOutputDevices.FirstOrDefault();

if (firstInput.Name != null && firstOutput.Name != null)
{
    Console.WriteLine($"\nCreating a route from '{firstInput.Name}' to '{firstOutput.Name}'.");
    Console.WriteLine("Play some notes on your MIDI keyboard. They should be sent to the output device.");

    // 5. Create a route using the MidiManager.
    // The MidiManager automatically handles device initialization.
    MidiRoute route = engine.MidiManager.CreateRoute(firstInput, firstOutput);

    // Add a modifier to the route (e.g., transpose up one octave).
    // route.AddProcessor(new TransposeModifier(12));

    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();

    // 6. Clean up the route.
    engine.MidiManager.RemoveRoute(route);
}
else
{
    Console.WriteLine("\nCould not find MIDI input and/or output devices to create a route.");
}

// The engine's Dispose() method will automatically clean up the backend.
```

#### Example 2: Playing a MIDI File with the Sequencer

This example demonstrates how to use SoundFlow's `Sequencer` to play a `.mid` file through an internal `Synthesizer`.

```csharp
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Metadata.Midi;
using SoundFlow.Midi.PortMidi;
using SoundFlow.Providers;
using SoundFlow.Structs;
using SoundFlow.Synthesis;
using SoundFlow.Synthesis.Banks;

// 1. Initialize audio engine and enable MIDI backend (optional for this example, but good practice).
using var engine = new MiniAudioEngine();
engine.UsePortMidi();

// 2. Initialize an audio playback device (your speakers).
using var audioDevice = engine.InitializePlaybackDevice(engine.PlaybackDevices.FirstOrDefault(d => d.IsDefault), AudioFormat.DvdHq);

// 3. Load the MIDI file and create a data provider.
var midiFile = MidiFileParser.Parse(File.OpenRead("path/to/your/song.mid"));
var midiDataProvider = new MidiDataProvider(midiFile);

// 4. Create an instrument bank and a synthesizer to generate audio.
var instrumentBank = new SoundFontBank("path/to/your/soundfont.sf2", audioDevice.Format); // or just use BasicInstrumentBank(audioDevice.Format);
var synthesizer = new Synthesizer(engine, audioDevice.Format, instrumentBank);

// 5. Create a Sequencer to drive the Synthesizer with MIDI data.
var sequencer = new Sequencer(engine, audioDevice.Format, midiDataProvider, synthesizer);

// 6. Add both of synthesizer and sequencer to the mixer.
audioDevice.MasterMixer.AddComponent(sequencer); // The Sequencer needs to be in the graph for its timing logic to run.
audioDevice.MasterMixer.AddComponent(synthesizer); // The Synthesizer needs to be in the graph for its audio to be heard.

// 7. Start playback.
audioDevice.Start();
sequencer.Play();

Console.WriteLine("Playing MIDI file... Press any key to stop.");
Console.ReadKey();

sequencer.Stop();
audioDevice.Stop();
```

#### Example 3: Live MIDI Input to Synthesizer

This example demonstrates how to route a live MIDI keyboard to an internal `Synthesizer` and hear the output through your speakers.

```csharp
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Midi.PortMidi;
using SoundFlow.Structs;
using SoundFlow.Synthesis;
using SoundFlow.Synthesis.Banks;

// 1. Initialize engines. PortMidi is required for MIDI input.
using var engine = new MiniAudioEngine();
engine.UsePortMidi();
engine.UpdateMidiDevicesInfo();

var midiInput = engine.MidiInputDevices.FirstOrDefault();
if (midiInput.Name == null)
{
    Console.WriteLine("No MIDI input device found.");
    return;
}

// 2. Initialize an audio playback device.
using var audioDevice = engine.InitializePlaybackDevice(null, AudioFormat.DvdHq);

// 3. Create an instrument bank and a synthesizer.
var instrumentBank = new SoundFontBank("path/to/your/soundfont.sf2", audioDevice.Format); // or just use BasicInstrumentBank(audioDevice.Format);
var synthesizer = new Synthesizer(engine, audioDevice.Format, instrumentBank);

// 4. Create the audio path: Add the Synthesizer to the device's master mixer.
audioDevice.MasterMixer.AddComponent(synthesizer);

// 5. Create the MIDI control path: Route the physical input device to the synthesizer instance.
MidiRoute route = engine.MidiManager.CreateRoute(midiInput, synthesizer);

// 6. Start the audio device to begin processing sound.
audioDevice.Start();

Console.WriteLine($"Ready to play! MIDI input from '{midiInput.Name}' is routed to the internal synthesizer.");
Console.WriteLine("Press any key to exit.");
Console.ReadKey();

// 7. Clean up.
engine.MidiManager.RemoveRoute(route);
audioDevice.Stop();
```

#### Example 4: MIDI Clock Synchronization (Master Mode)

This example demonstrates how to configure SoundFlow to send MIDI Clock to an external device, synchronized with a composition's playback.

```csharp
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Editing;
using SoundFlow.Midi.PortMidi;
using SoundFlow.Midi.PortMidi.Enums;
using SoundFlow.Structs;

using var engine = new MiniAudioEngine();
var midiBackend = engine.UsePortMidi();
engine.UpdateMidiDevicesInfo();

var syncOutputDevice = engine.MidiOutputDevices.FirstOrDefault();
if (syncOutputDevice.Name == null)
{
    Console.WriteLine("No MIDI output device found for sync.");
    return;
}

// 1. Create a Composition. Its renderer will be our master clock source.
var composition = new Composition(engine, AudioFormat.Dvd);
// ... add tracks and segments to your composition ...

// 2. Configure the MIDI backend for Master synchronization.
midiBackend.ConfigureSync(
    mode: SyncMode.Master,
    source: SyncSource.Internal, // Not used in Master mode
    inputDeviceInfo: null,
    outputDeviceInfo: syncOutputDevice,
    renderer: composition.Renderer // Link to the composition's transport
);

// 3. Use a SoundPlayer to play the composition through an audio device.
using var audioDevice = engine.InitializePlaybackDevice(null, AudioFormat.Dvd);
var player = new SoundPlayer(engine, audioDevice.Format, composition.Renderer);
audioDevice.MasterMixer.AddComponent(player);

Console.WriteLine($"Sending MIDI Clock to '{syncOutputDevice.Name}'. Press any key to start/stop playback.");
Console.ReadKey();

// 4. Start playback. The sync manager will automatically send MIDI Start and Clock messages.
player.Play();
audioDevice.Start();
Console.WriteLine("Playback started. Sending clock...");

Console.ReadKey();

// 5. Stop playback. The sync manager will send a MIDI Stop message.
player.Stop();
audioDevice.Stop();
Console.WriteLine("Playback stopped.");
```

## Origin and Licensing

This `SoundFlow.Midi.PortMidi` package consists of C# wrapper code and relies on a separate native binary of the **PortMidi** library.

*   The C# code within this package is licensed under the **MIT License**.
*   The native `portmidi` library is licensed under its own **MIT-style license**. The text of the PortMidi license is included with its source distributions.

**Users of this package must comply with the terms of BOTH licenses.** Please consult the native library's specific distribution for its exact licensing requirements.

## Contributing

Contributions to `SoundFlow.Midi.PortMidi` are welcome! Please open issues or submit pull requests to the main SoundFlow repository following the general [SoundFlow Contributing Guidelines](https://github.com/LSXPrime/SoundFlow#contributing).

## Acknowledgments

We gratefully acknowledge the work of the **PortMidi team** for creating and maintaining this reliable, cross-platform MIDI library, which makes this integration possible.

## License

The C# code in `SoundFlow.Midi.PortMidi` is licensed under the [MIT License](../../LICENSE.md).