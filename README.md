<div align="center">

> ⚠️ **Project Status:** The maintainer is on hiatus from Jan 2026 to Feb 2027. Support and updates will be limited. 
> 
> [**Read the full announcement for details.**](https://github.com/LSXPrime/SoundFlow/discussions/102)

[![SoundFlow Logo](https://raw.githubusercontent.com/LSXPrime/SoundFlow/refs/heads/master/logo.png)](https://github.com/LSXPrime/SoundFlow)

# SoundFlow

**The Complete .NET Audio Framework: From High-Fidelity Synthesis to Secure Distribution**

[![Build Status](https://github.com/LSXPrime/SoundFlow/actions/workflows/release.yml/badge.svg)](https://github.com/LSXPrime/SoundFlow/actions/workflows/release.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![NuGet](https://img.shields.io/nuget/v/SoundFlow.svg)](https://www.nuget.org/packages/SoundFlow) [![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

</div>

[![Stand With Palestine](https://raw.githubusercontent.com/Safouene1/support-palestine-banner/master/banner-support.svg)](https://thebsd.github.io/StandWithPalestine)
<div align="center">
  <p><strong>This project stands in solidarity with the people of Palestine and condemns the ongoing violence and ethnic cleansing by Israel. We believe developers have a responsibility to be aware of such injustices. Read our full statement on the catastrophic situation in Palestine and the surrounding region.</strong></p>
  <a href="STATEMENT.md"><kbd>Read Our Full Ethical Stance</kbd></a>
</div>
<br>

## Introduction

SoundFlow is a robust and versatile .NET audio engine designed for seamless cross-platform audio processing. It provides a comprehensive set of features for audio playback, recording, processing, analysis, and visualization, all within a well-structured and extensible framework. SoundFlow empowers developers to build sophisticated audio applications, from real-time communication systems to advanced non-linear audio editors.

## Key Features

SoundFlow provides a comprehensive suite of tools organized into a powerful, extensible architecture.

### Core Architecture & Design
*   **Cross-Platform Compatibility:** Runs seamlessly on Windows, macOS, Linux, Android, iOS, and FreeBSD, ensuring broad deployment options.
*   **High Performance:** Optimized for real-time audio processing with SIMD support and efficient memory management.
*   **Modular Component Architecture:** Build custom audio pipelines by connecting sources, modifiers, mixers, and analyzers.
*   **Extensibility:** Easily add custom audio components, effects, and visualizers to tailor the engine to your specific needs.
*   **Plug & Play Integrations:** Extend SoundFlow's capabilities with official integration packages, such as the WebRTC Audio Processing Module.
*   **Backend Agnostic:** Supports the `MiniAudio` backend out of the box, with the ability to add others.

### Advanced Audio I/O & Device Management
*   **Multi-Device Management:** Initialize and manage multiple independent audio playback and capture devices simultaneously, each with its own audio graph.
*   **Advanced Device Control:** Fine-tune latency, sharing modes, and platform-specific settings (WASAPI, CoreAudio, ALSA, etc.) for professional-grade control.
*   **On-the-fly Device Switching:** Seamlessly switch between audio devices during runtime without interrupting the audio graph.

### Core Audio Processing & Playback
*   **Playback:** Play audio from various sources, including files, streams, and in-memory assets.
*   **Recording:** Capture audio input and save it to different encoding formats.
*   **Mixing:** Combine multiple audio streams with precise control over volume and panning.
*   **Effects:** Apply a wide range of audio effects, including reverb, chorus, delay, equalization, and more.

### Analysis, Formats & Streaming
*   **Pluggable Codec System:** Extend format support dynamically via `ICodecFactory`. Includes built-in support for WAV, MP3, and FLAC (via MiniAudio), with extensive format support available via extensions.
*   **Robust Metadata Handling:** Read and write metadata tags (ID3v1, ID3v2, Vorbis Comments, MP4 Atoms) and embedded Cue Sheets for a wide range of formats (MP3, FLAC, OGG, M4A, WAV, AIFF).
*   **Visualization & Analysis:** Create engaging visual representations with FFT-based spectrum analysis, voice activity detection, and level metering.
*   **Surround Sound:** Supports advanced surround sound configurations with customizable speaker positions, delays, and panning methods.
*   **HLS Streaming Support:** Integrate internet radio and online audio via HTTP Live Streaming.

### Synthesis Engine
*   **Polyphonic Synthesizer:** A robust synthesis engine supporting unison, filtering, and modulation envelopes.
*   **SoundFont Support:** Native loading and playback of SoundFont 2 (.sf2) banks.
*   **MPE Support:** Full support for MIDI Polyphonic Expression for per-note control of pitch, timbre, and pressure.

### MIDI Ecosystem
*   **Cross-Platform I/O:** Send and receive MIDI messages from hardware devices via the PortMidi backend.
*   **Routing & Effects:** Graph-based MIDI routing with a suite of modifiers including Arpeggiators, Harmonizers, Randomizers, and Velocity curves.
*   **Parameter Mapping:** Real-time MIDI mapping system allows controlling any engine parameter (Volume, Filter Cutoff, etc.) via external hardware controllers.

### Non-Destructive Audio & MIDI Editing
*   **Compositions & Tracks:** Organize projects into multi-track compositions supporting both Audio and MIDI tracks.
*   **Hybrid Timeline:** Mix audio clips and MIDI segments on the same timeline.
*   **Sequencing:** Sample-accurate MIDI sequencing with quantization, swing, and tempo map support.
*   **Project Persistence:** Save/Load full projects including audio assets, MIDI sequences, tempo maps, and routing configurations, with optional digital signing for integrity.

### Comprehensive Security Suite
*   **Audio Encryption:** High-performance, seekable stream encryption using AES-256-CTR, packaged in a secure container format.
*   **Digital Signatures:** Ensure file integrity and authenticity for projects and audio containers using ECDSA digital signatures.
*   **Audio Watermarking:** Embed robust, inaudible ownership data (DSSS) or fragile integrity verification markers (LSB) directly into audio signals.
*   **Acoustic Fingerprinting:** Identify audio content by generating and matching robust acoustic fingerprints against a database.
## Getting Started

To begin using SoundFlow, the easiest way is to install the NuGet package:

```bash
dotnet add package SoundFlow
```

For a minimal working example of how to set up an audio device and play a simple sound, please refer to the starter guide on the official documentation homepage: **[SoundFlow Minimal Example](https://lsxprime.github.io/soundflow-docs/#/docs/latest/getting-started)**.

You can also find a wide variety of practical applications, complex audio graphs, and feature usage examples in the [Samples](https://github.com/LSXPrime/SoundFlow/tree/master/Samples) folder of the repository.

## Extensions

SoundFlow's architecture supports adding specialized audio processing capabilities via dedicated NuGet packages. These extensions integrate external libraries, making their features available within the SoundFlow ecosystem.

### SoundFlow.Codecs.FFMpeg
This package integrates the massive **FFmpeg** library into SoundFlow. While the core engine handles common formats, this extension unlocks decoding and encoding for virtually any audio format in existence.

*   **Decoders/Encoders:** Adds support for MP3 (encoder by LAME), AAC, OGG Vorbis, Opus, ALAC, AC3, PCM variations, and many more.
*   **Container Support:** Handles complex containers like M4A, MKA, and others.
*   **Automatic Registration:** simply registering the factory enables the engine to auto-detect and play these formats transparently.

To install this extension:
```bash
dotnet add package SoundFlow.Codecs.FFMpeg
```

### SoundFlow.Midi.PortMidi
This package provides the backend implementation for MIDI hardware I/O using **PortMidi**.

*   **Hardware Access:** Enumerates and connects to physical MIDI keyboards, synthesizers, and controllers on Windows, macOS, and Linux.
*   **Synchronization:** Provides high-precision clock synchronization, allowing SoundFlow to act as a MIDI Clock Master or Slave.

To install this extension:
```bash
dotnet add package SoundFlow.Midi.PortMidi
```

### SoundFlow.Extensions.WebRtc.Apm
This package provides an integration with a native library based on the **WebRTC Audio Processing Module (APM)**. The WebRTC APM is a high-quality suite of algorithms commonly used in voice communication applications to improve audio quality.

Features included in this extension:
*   **Acoustic Echo Cancellation (AEC):** Reduces echoes caused by playback audio being picked up by the microphone.
*   **Noise Suppression (NS):** Reduces steady-state background noise.
*   **Automatic Gain Control (AGC):** Automatically adjusts the audio signal level to a desired target.
*   **High Pass Filter (HPF):** Removes low-frequency components (like DC offset or rumble).
*   **Pre-Amplifier:** Applies a fixed gain before other processing.

**Note:** The WebRTC APM native library has specific requirements, notably supporting only certain sample rates (8000, 16000, 32000, or 48000 Hz). Ensure your audio devices are initialized with one of these rates when using this extension.

To install this extension:
```bash
dotnet add package SoundFlow.Extensions.WebRtc.Apm
```

## API Reference

Comprehensive API documentation will be available on the **[SoundFlow Documentation](https://lsxprime.github.io/soundflow-docs/)**.

## Tutorials and Examples

The **[Documentation](https://lsxprime.github.io/soundflow-docs/)** provides a wide range of tutorials and examples to help you get started:

*   **Playback:** Playing audio files and streams, controlling playback.
*   **Synthesis:** Loading SoundFonts, creating synthesizers, and handling MIDI events.
*   **Recording:** Recording audio and MIDI, using voice activity detection.
*   **Effects:** Applying various audio effects and MIDI modifiers (Arpeggiator, Harmonizer).
*   **Analysis:** Getting RMS level, analyzing frequency spectrum.
*   **Visualization:** Creating level meters, waveform displays, and spectrum analyzers.
*   **Composition:** Managing audio projects, including creating, editing, and saving multi-track compositions.
*   **Security:** Encrypting audio, signing files, and embedding robust ownership watermarks.

**(Note:** You can also find extensive example code in the `Samples` folder of the repository.)

## Contributing

We deeply appreciate your interest in improving SoundFlow.

For detailed guidelines on how to report bugs, suggest features, and submit pull requests, please consult the **[CONTRIBUTING.md](CONTRIBUTING.md)** file for more information.

## Acknowledgments

We sincerely appreciate the foundational work provided by the following projects and modules:

*   **[miniaudio](https://github.com/mackron/miniaudio)** - Provides a lightweight and efficient audio I/O backend.
*   **[FFmpeg](https://ffmpeg.org/)** - The leading multimedia framework, powering our codec extension.
*   **[LAME Project](https://lame.sourceforge.io/)** - For the high-quality MP3 encoder used in the FFMpeg extension.
*   **[PortMidi](https://github.com/PortMidi/portmidi)** - Enables cross-platform MIDI I/O.
*   **[WebRTC Audio Processing Module (APM)](https://gitlab.freedesktop.org/pulseaudio/webrtc-audio-processing)** - Offers advanced audio processing (AEC, AGC, Noise Suppression).

## Support This Project

SoundFlow is an open-source project driven by passion and community needs. Maintaining and developing a project of this scale requires significant time and resources.

Your support is crucial for the continued development and maintenance of SoundFlow. Contributions help dedicate more time to the project, improve documentation, and acquire necessary hardware for robust testing. For instance, funds will directly help purchase dedicated audio equipment for more accurate testing, moving beyond basic built-in speakers to ensure high-quality output for everyone.

If you find this project useful, please consider one of the following ways to support it:

*   **[❤️ Sponsor on Ko-fi](https://ko-fi.com/lsxprime)** - For simple one-time or recurring donations.
*   **[💸 Donate via PayPal](https://paypal.me/LSXPrime)** - For quick and easy one-time contributions.
*   **[🌐 Donate using AirTM](https://airtm.me/lsxprime)** - Offers various payment options like Bank Transfer, Debit/Credit Card, and more.
*   **[💎 USDT (Tron/TRC20)](https://github.com/LSXPrime/SoundFlow#)** - Send to the following wallet address: `TKZzeB71XacY3Av5rnnQVrz2kQqgzrkjFn`
    *   **Important:** Please ensure you are sending USDT via the **TRC20 (Tron)** network. Sending funds on any other network may result in their permanent loss.

**Thank you for your generosity and for helping ensure SoundFlow sounds great for everyone!**

## License

SoundFlow is released under the [MIT License](LICENSE.md).

## Citing SoundFlow

If you use SoundFlow in your research or project, you can cite it using the following format:

### APA
```
Abdallah, A. (2026). SoundFlow: A high-performance, secure audio and MIDI engine for .NET (Version 1.4.0) [Computer software]. https://github.com/LSXPrime/SoundFlow
```

### BibTeX
```
@software{abdallah_soundflow_2026,
  author = {Abdallah, Ahmed},
  title = {{SoundFlow: A high-performance, secure audio and MIDI engine for .NET}},
  url = {https://github.com/LSXPrime/SoundFlow},
  version = {1.4.0},
  year = {2026},
  note = {Cross-platform audio processing, synthesis, and content protection framework}
}
```