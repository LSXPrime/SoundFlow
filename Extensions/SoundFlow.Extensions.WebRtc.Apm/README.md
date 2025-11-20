<div align="center">
    <img src="https://raw.githubusercontent.com/LSXPrime/SoundFlow/refs/heads/master/logo.png" alt="Project Logo" width="256" height="256">

# SoundFlow - Audio Processing Module Extension (WebRTC)

**WebRTC Audio Processing Module (Google & PulseAudio) Integration for SoundFlow**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![NuGet](https://img.shields.io/nuget/v/SoundFlow.Extensions.WebRtc.Apm.svg)](https://www.nuget.org/packages/SoundFlow.Extensions.WebRtc.Apm)
[![SoundFlow Main Repository](https://img.shields.io/badge/SoundFlow%20Core-Repo-blue)](https://github.com/LSXPrime/SoundFlow)

</div>

## Introduction

`SoundFlow.Extensions.WebRtc.Apm` is an official extension package for the [SoundFlow (.NET) audio engine](https://github.com/LSXPrime/SoundFlow). It integrates a native library based on the high-quality **WebRTC Audio Processing Module (APM)**, bringing advanced voice processing capabilities to your SoundFlow audio pipelines.

The WebRTC APM is widely used in real-time communication applications (like WebRTC itself) to enhance audio quality by addressing common issues such as echo, background noise, and varying audio levels.

## Features

This extension provides the following WebRTC APM features, configurable and applicable within your SoundFlow projects:

*   **Acoustic Echo Cancellation (AEC):** Effectively cancels echoes that occur when audio played through speakers is picked up by the microphone. Essential for speakerphone or conference call scenarios.
*   **Noise Suppression (NS):** Reduces unwanted steady-state background noise (e.g., fan noise, hum) from the audio signal, improving clarity.
*   **Automatic Gain Control (AGC):** Dynamically adjusts the microphone input volume to ensure a consistent and appropriate level, preventing audio that is too quiet or clipped.
*   **High Pass Filter (HPF):** Removes low-frequency components below ~80 Hz, helping to eliminate DC offset, rumble, and plosives.
*   **Pre-Amplifier:** Applies a configurable gain to the audio signal before other APM processing steps.

These features are primarily applied via a single, unified `WebRtcApmModifier` component designed for real-time audio graph processing. The package also includes a `NoiseSuppressor` component for convenient offline/batch processing of `ISoundDataProvider` sources.

## Getting Started

### Installation

This package requires SoundFlow (the core library). You can install it via NuGet:

**NuGet Package Manager:**

```bash
Install-Package SoundFlow.Extensions.WebRtc.Apm
```

**.NET CLI:**

```bash
dotnet add package SoundFlow.Extensions.WebRtc.Apm
```

### Usage

The primary way to use the WebRTC APM features in a real-time SoundFlow graph is by adding the `WebRtcApmModifier` to a `SoundComponent`, typically one that represents your audio input source (e.g., a component connected to a `MicrophoneDataProvider`).

```csharp
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Providers;
using SoundFlow.Enums;
using SoundFlow.Extensions.WebRtc.Apm.Modifiers;

// Initialize the audio engine with mixed capability
var audioEngine = new MiniAudioEngine(48000, Capability.Mixed, channels: 1);


// --- Real-time Processing ---
// For real-time audio processing and voice communication applications:, add the WebRtcApmModifier to a SoundComponent.

// Create the microphone data provider and sound player
var microphoneDataProvider = new MicrophoneDataProvider();
var soundPlayer = new SoundPlayer(microphoneDataProvider);

// Create and configure the WebRTC APM (Audio Processing Module) modifier
var apmModifier = new WebRtcApmModifier(
    // Echo Cancellation (AEC) settings
    aecEnabled: true,          // Enable echo cancellation to remove feedback from speakers
    aecMobileMode: false,      // Use desktop-optimized AEC (false) vs mobile-optimized (true)
    aecLatencyMs: 40,         // Expected system latency in milliseconds for echo cancellation

    // Noise Suppression settings
    nsEnabled: true,           // Enable background noise reduction
    nsLevel: NoiseSuppressionLevel.High,  // Aggressiveness of noise suppression

    // Automatic Gain Control (AGC) settings - Version 1
    agc1Enabled: true,         // Enable legacy AGC (Automatic Gain Control)
    agcMode: GainControlMode.AdaptiveDigital,  // AGC operating mode
    agcTargetLevel: -3,        // Target output level in dBFS (-31 to 0) for Adaptive mode
    agcCompressionGain: 9,     // Fixed gain in dB (0-90) for FixedDigital mode
    agcLimiter: true,          // Enable output limiter to prevent clipping

    // AGC Version 2 settings (newer algorithm)
    agc2Enabled: false,        // Disable newer AGC implementation

    // Filter settings
    hpfEnabled: true,          // Enable high-pass filter to remove low-frequency noise
    preAmpEnabled: false,      // Disable pre-amplifier stage
    preAmpGain: 1.0f,          // Default gain (1.0 = no amplification)

    // Multi-channel audio processing settings
    useMultichannelCapture: false,  // Process capture audio as mono/stereo (false)
    useMultichannelRender: false,   // Process render audio as mono/stereo (false)
    downmixMethod: DownmixMethod.AverageChannels  // How to mix channels when downmixing
);

// Add the modifier to the sound player
soundPlayer.AddModifier(apmModifier);

// Add sound player to the master mixer
Mixer.Master.AddComponent(soundPlayer);

// Start capturing and playing audio
microphoneDataProvider.StartCapture();
soundPlayer.Play();

Console.WriteLine("Audio processing with WebRTC APM is running.");
Console.WriteLine("Press keys to modify settings:");
Console.WriteLine("  [A] Toggle AEC");
Console.WriteLine("  [M] Toggle AEC Mobile Mode");
Console.WriteLine("  [N] Toggle Noise Suppression");
Console.WriteLine("  [L] Cycle Noise Suppression Level");
Console.WriteLine("  [1] Toggle AGC1");
Console.WriteLine("  [2] Toggle AGC2");
Console.WriteLine("  [H] Toggle High Pass Filter");
Console.WriteLine("  [P] Toggle Pre-Amplifier");
Console.WriteLine("  [+] Increase Pre-Amp Gain");
Console.WriteLine("  [-] Decrease Pre-Amp Gain");
Console.WriteLine("  [ESC] Exit");

bool running = true;
while (running)
{
    var key = Console.ReadKey(true).Key;

    switch (key)
    {
        case ConsoleKey.A:
            apmModifier.EchoCancellation.Enabled = !apmModifier.EchoCancellation.Enabled;
            Console.WriteLine($"AEC Enabled: {apmModifier.EchoCancellation.Enabled}");
            break;

        case ConsoleKey.M:
            apmModifier.EchoCancellation.MobileMode = !apmModifier.EchoCancellation.MobileMode;
            Console.WriteLine($"AEC Mobile Mode: {apmModifier.EchoCancellation.MobileMode}");
            break;

        case ConsoleKey.N:
            apmModifier.NoiseSuppression.Enabled = !apmModifier.NoiseSuppression.Enabled;
            Console.WriteLine($"Noise Suppression Enabled: {apmModifier.NoiseSuppression.Enabled}");
            break;

        case ConsoleKey.L:
            var currentLevel = (int)apmModifier.NoiseSuppression.Level;
            var nextLevel = (NoiseSuppressionLevel)((currentLevel + 1) % Enum.GetValues(typeof(NoiseSuppressionLevel)).Length);
            apmModifier.NoiseSuppression.Level = nextLevel;
            Console.WriteLine($"Noise Suppression Level: {apmModifier.NoiseSuppression.Level}");
            break;

        case ConsoleKey.D1:
            apmModifier.AutomaticGainControl.Agc1Enabled = !apmModifier.AutomaticGainControl.Agc1Enabled;
            Console.WriteLine($"AGC1 Enabled: {apmModifier.AutomaticGainControl.Agc1Enabled}");
            break;

        case ConsoleKey.D2:
            apmModifier.AutomaticGainControl.Agc2Enabled = !apmModifier.AutomaticGainControl.Agc2Enabled;
            Console.WriteLine($"AGC2 Enabled: {apmModifier.AutomaticGainControl.Agc2Enabled}");
            break;

        case ConsoleKey.H:
            apmModifier.HighPassFilterEnabled = !apmModifier.HighPassFilterEnabled;
            Console.WriteLine($"High Pass Filter Enabled: {apmModifier.HighPassFilterEnabled}");
            break;

        case ConsoleKey.P:
            apmModifier.PreAmplifierEnabled = !apmModifier.PreAmplifierEnabled;
            Console.WriteLine($"Pre-Amplifier Enabled: {apmModifier.PreAmplifierEnabled}");
            break;

        case ConsoleKey.Add:
        case ConsoleKey.OemPlus:
            apmModifier.PreAmplifierGainFactor += 0.5f;
            Console.WriteLine($"Pre-Amp Gain: {apmModifier.PreAmplifierGainFactor}");
            break;

        case ConsoleKey.Subtract:
        case ConsoleKey.OemMinus:
            apmModifier.PreAmplifierGainFactor = Math.Max(0.5f, apmModifier.PreAmplifierGainFactor - 0.5f);
            Console.WriteLine($"Pre-Amp Gain: {apmModifier.PreAmplifierGainFactor}");
            break;

        case ConsoleKey.Escape:
            running = false;
            break;
    }
}

// Clean up
microphoneDataProvider.StopCapture();
soundPlayer.Stop();
Mixer.Master.RemoveComponent(soundPlayer);
microphoneDataProvider.Dispose();
audioEngine.Dispose();

Console.WriteLine("Audio processing stopped.");

// --- Offline Processing ---
// For batch processing of an entire audio source without the real-time graph:

using SoundFlow.Extensions.WebRtc.Apm.Components; // For NoiseSuppressor
ISoundDataProvider sourceForOffline = ...; // e.g., new AssetDataProvider(...) or ChunkedDataProvider(...)

// Ensure source data matches the sampleRate and channels used here!
try
{
    using var offlineSuppressor = new NoiseSuppressor(sourceForOffline, 48000, 1, NoiseSuppressionLevel.VeryHigh); 
    var encoder = AudioEngine.Instance.CreateEncoder(CleanedFilePath, "wav", SampleFormat.F32, 1, 48000);

    // Option 1: Process all into a single array (for smaller files) - See Samples\SoundFlow.Samples.NoiseSuppression
    // float[] cleanedAudio = offlineSuppressor.ProcessAll(); 

    // Option 2: Process chunk-by-chunk (for large files)
    offlineSuppressor.OnAudioChunkProcessed += (chunk) =>
    {
        if (encoder.IsDisposed == false)
            encoder.Encode(chunk.ToArray()); // Handle the processed 'chunk' (ReadOnlyMemory<float>), encode to file
    };
    offlineSuppressor.ProcessChunks(); // This method blocks until done
    encoder.Dispose(); // Dispose encoder to save the cleaned audio
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Offline processing failed: {ex.Message}");
}

```

## Sample

A sample application demonstrating the usage of the `NoiseSuppressor` component for offline audio file processing can be found here:

[SoundFlow.Samples.NoiseSuppression](https://github.com/LSXPrime/SoundFlow/blob/master/SoundFlow.Samples.NoiseSuppression/) *(Note: This link assumes the sample is in the main SoundFlow repository)*

## Origin and Licensing

This `SoundFlow.Extensions.WebRtc.Apm` package consists of C# code (wrapper, integration logic, modifiers, components) and relies on a separate native binary derived from the **WebRTC Audio Processing Module**.

*   The C# code within this `SoundFlow.Extensions.WebRtc.Apm` package is licensed under the **MIT License**.
*   The native `webrtc_audio_processing` library wrapped by this package is based on the efforts of the [PulseAudio project](https://gitlab.freedesktop.org/pulseaudio/webrtc-audio-processing) to extract the APM from the full WebRTC source tree into a standalone, buildable library.
*   The underlying WebRTC Audio Processing Module code is typically licensed under the **BSD 3-Clause "New" or "Revised" License**.

**Users of this package must comply with the terms of BOTH the MIT License (for the C# wrapper) and the BSD 3-Clause License (for the native library).** The BSD license generally requires including the copyright notice and license text of the WebRTC code. Please consult the native library's specific distribution for the exact licensing requirements.

## Contributing

Contributions to the `SoundFlow.Extensions.WebRtc.Apm` package are welcome! Please open issues or submit pull requests to this repository following the general [SoundFlow Contributing Guidelines](https://github.com/LSXPrime/SoundFlow#contributing).

## Acknowledgments

We gratefully acknowledge the work of the original **WebRTC team** for developing the powerful Audio Processing Module, and the **PulseAudio project** for their valuable effort in extracting and maintaining a standalone version of the APM library, which makes this integration possible.

## License

The C# code in `SoundFlow.Extensions.WebRtc.Apm` is licensed under the [MIT License](../../LICENSE.md).