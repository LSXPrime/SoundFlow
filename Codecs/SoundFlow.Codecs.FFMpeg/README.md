<div align="center">
    <img src="https://raw.githubusercontent.com/LSXPrime/SoundFlow/refs/heads/master/logo.png" alt="Project Logo" width="256" height="256">

# SoundFlow - Codec Extension (FFmpeg)

**Extensive Audio Format Support for SoundFlow using FFmpeg**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![NuGet](https://img.shields.io/nuget/v/SoundFlow.Codecs.FFMpeg.svg)](https://www.nuget.org/packages/SoundFlow.Codecs.FFMpeg)
[![SoundFlow Main Repository](https://img.shields.io/badge/SoundFlow%20Core-Repo-blue)](https://github.com/LSXPrime/SoundFlow)

</div>

## Introduction

`SoundFlow.Codecs.FFMpeg` is an official codec extension package for the [SoundFlow (.NET) audio engine](https://github.com/LSXPrime/SoundFlow). It integrates a custom, lightweight native library built on the powerful **[FFmpeg](https://ffmpeg.org/)** framework to provide decoding and encoding support for a vast range of audio formats.

By registering this extension with the SoundFlow engine, you can seamlessly load, play, and write formats like MP3, AAC, OGG, Opus, and many more, which are not supported by the core library's default codecs.

## Features

This extension provides a high-performance and memory-efficient bridge to FFmpeg's audio capabilities:

*   **Broad Codec Support:** Adds support for dozens of popular audio formats, including:
    *   **Lossy:** MP3 (encoded via LAME), AAC, Ogg Vorbis, Opus, WMA, AC3
    *   **Lossless:** FLAC, ALAC (Apple Lossless), APE, WavPack (WV), TTA
    *   **And many more container and raw formats.**
*   **Seamless Integration:** Implements a high-priority `ICodecFactory`, allowing SoundFlow to automatically use FFmpeg for supported formats with no changes to your existing playback or recording code.
*   **High Performance & Efficiency:** Works directly with streams using a callback-based native wrapper. This avoids loading entire audio files into memory, making it ideal for large files and network streams.
*   **Cross-Platform:** Includes pre-compiled native binaries for Windows, macOS, Linux, Android, iOS and FreeBSD (x64, x86, ARM64), ensuring it works wherever SoundFlow runs.
*   **Automatic Format Conversion:** The native wrapper intelligently uses FFmpeg's `swresample` library to automatically convert audio from its source format to the format required by your application (e.g., 32-bit float), simplifying your audio pipeline.

## Getting Started

### Installation

This package requires the core SoundFlow library. Install it via NuGet:

**NuGet Package Manager:**

```bash
Install-Package SoundFlow.Codecs.FFMpeg
```

**.NET CLI:**

```bash
dotnet add package SoundFlow.Codecs.FFMpeg
```

### Usage

To enable FFmpeg support, you simply need to register the `FFmpegCodecFactory` with your `AudioEngine` instance upon initialization. Once registered, SoundFlow will automatically use it for all supported file types.

```csharp
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Codecs.FFMpeg; // 1. Import the FFmpeg codec namespace
using SoundFlow.Components;
using SoundFlow.Providers;
using SoundFlow.Structs;

// 2. Initialize the Audio Engine.
using var engine = new MiniAudioEngine();

// 3. Register the FFmpeg Codec Factory.
// This single line enables support for all FFmpeg formats.
engine.RegisterCodecFactory(new FFmpegCodecFactory());

// From here, the usage is standard SoundFlow.
// The engine will now automatically use FFmpeg when it encounters an MP3 file.

// Initialize a playback device.
using var device = engine.InitializePlaybackDevice(null, AudioFormat.DvdHq);

// Create a SoundPlayer with a StreamDataProvider for an MP3 file.
var player = new SoundPlayer(engine, device.Format,
    new StreamDataProvider(engine, File.OpenRead("path/to/your/audio.mp3")));

// Add the player to the device's MasterMixer.
device.MasterMixer.AddComponent(player);

// Start playback.
device.Start();
player.Play();

Console.WriteLine("Playing MP3 file using FFmpeg... Press any key to stop.");
Console.ReadKey();

// Clean up.
player.Stop();
device.Stop();
```

## Technical Details

The native library included in this package is a custom-built, lightweight wrapper around FFmpeg and the LAME MP3 encoder. To minimize binary size, it is configured with a "disable-all, enable-specific" strategy. The build includes a curated set of audio-only components and excludes all video processing, hardware acceleration, networking protocols (except `file` and `pipe`), and other non-essential features.

This results in a small, focused, and highly efficient native dependency tailored specifically for SoundFlow's audio processing needs.

## Origin and Licensing

This `SoundFlow.Codecs.FFMpeg` package consists of C# wrapper code and a custom native library that statically links against FFmpeg and LAME libraries.

*   The C# code within this `SoundFlow.Codecs.FFMpeg` package is licensed under the **MIT License**.
*   The included native library builds upon FFmpeg and LAME, both of which are licensed under the **LGPL v2.1 or later**. The FFmpeg build is configured with `--disable-gpl` and `--disable-nonfree` flags. Your use of this package must comply with the terms of the LGPL. This generally means that if you dynamically link to this library, you can use it in proprietary software, but if you modify the FFmpeg or LAME source code itself, you must release those changes.

**Users of this package must comply with the terms of BOTH the MIT License (for the C# wrapper) and the LGPL (for the underlying FFmpeg and LAME components).** For detailed information, please consult the official [FFmpeg Licensing Page](https://ffmpeg.org/legal.html) and the [LAME Project Website](https://lame.sourceforge.io/).

## Contributing

Contributions to `SoundFlow.Codecs.FFMpeg` are welcome! Please open issues or submit pull requests to the main SoundFlow repository following the general [SoundFlow Contributing Guidelines](https://github.com/LSXPrime/SoundFlow#contributing).

## Acknowledgments

This package would not be possible without the incredible work of the **FFmpeg project team and its contributors**. Special thanks to the **LAME project** for the high-quality MP3 encoder.

## License

The C# code in `SoundFlow.Codecs.FFMpeg` is licensed under the [MIT License](../../LICENSE.md).