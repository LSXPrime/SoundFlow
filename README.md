<div align="center">
    <img src="https://raw.githubusercontent.com/LSXPrime/SoundFlow/refs/heads/master/logo.png" alt="Project Logo" width="256" height="256">

# SoundFlow

**A Powerful and Extensible .NET Audio Engine for Enterprise Applications**

[![Build Status](https://github.com/LSXPrime/SoundFlow/actions/workflows/build.yml/badge.svg)](https://github.com/LSXPrime/SoundFlow/actions/workflows/build.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![NuGet](https://img.shields.io/nuget/v/SoundFlow.svg)](https://www.nuget.org/packages/SoundFlow) [![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

</div>

[![Stand With Palestine](https://raw.githubusercontent.com/TheBSD/StandWithPalestine/main/banner-no-action.svg)](https://thebsd.github.io/StandWithPalestine)
<div align="center">
  <p><strong>This project stands in solidarity with the people of Palestine and condemns the ongoing violence and ethnic cleansing by Israel. We believe developers have a responsibility to be aware of such injustices. Read our full statement on the catastrophic situation in Palestine and the surrounding region.</strong></p>
  <a href="#an-ethical-stance"><kbd>Read Our Full Ethical Stance</kbd></a>
</div>
<br>

## Introduction

SoundFlow is a robust and versatile .NET audio engine designed for seamless cross-platform audio processing. It provides a comprehensive set of features for audio playback, recording, processing, analysis, and visualization, all within a well-structured and extensible framework. SoundFlow empowers developers to build sophisticated audio applications, from real-time communication systems to advanced non-linear audio editors.

**Key Features:**

*   **Cross-Platform Compatibility:** Runs seamlessly on Windows, macOS, Linux, Android, iOS, and FreeBSD, ensuring broad deployment options.
*   **Multi-Device Management:** Initialize and manage multiple independent audio playback and capture devices simultaneously, each with its own audio graph.
*   **Advanced Device Control:** Fine-tune latency, sharing modes, and platform-specific settings (WASAPI, CoreAudio, ALSA, etc.) for professional-grade control.
*   **On-the-fly Device Switching:** Seamlessly switch between audio devices during runtime without interrupting the audio graph.
*   **Modular Component Architecture:** Build custom audio pipelines by connecting sources, modifiers, mixers, and analyzers.
*   **Plug & Play Integrations:** Extend SoundFlow's capabilities with official integration packages, such as the WebRTC Audio Processing Module for advanced noise suppression, echo cancellation, and automatic gain control.
*   **Extensibility:** Easily add custom audio components, effects, and visualizers to tailor the engine to your specific needs.
*   **High Performance:** Optimized for real-time audio processing with SIMD support and efficient memory management.
*   **Playback:** Play audio from various sources, including files, streams, and in-memory assets.
*   **Recording:** Capture audio input and save it to different encoding formats.
*   **Mixing:** Combine multiple audio streams with precise control over volume and panning.
*   **Effects:** Apply a wide range of audio effects, including reverb, chorus, delay, equalization, and more.
*   **Analysis:** Extract valuable information from audio data, such as RMS level, peak level, frequency spectrum, and voice activity.
*   **Visualization:** Create engaging visual representations of audio waveforms, spectrums, and level meters.
*   **Surround Sound:** Supports advanced surround sound configurations with customizable speaker positions, delays, and panning methods.
*   **HLS Streaming Support:** Integrate internet radio and online audio via HTTP Live Streaming.
*   **Backend Agnostic:** Supports the `MiniAudio` backend out of the box, with the ability to add others.
*   **Non-Destructive Audio Editing & Persistence:**
    *   **Compositions & Tracks:** Organize audio projects into multi-track compositions, allowing flexible arrangement and mixing.
    *   **Audio Segments:** Place and manipulate audio clips on a timeline with granular control over their playback, including volume, pan, fades (Linear, Logarithmic, S-Curve), reversal, and advanced looping (repetitions or target duration).
    *   **Pitch-Preserved Time Stretching:** Modify the duration of audio segments without altering their pitch, ideal for adapting content to specific timeframes.
    *   **Project Save/Load:** Persist entire audio compositions to disk, including media consolidation and embedding options, with support for relinking missing audio sources.
    *   **Timeline Manipulation:** Programmatically insert, remove, replace, or silence audio sections on a track, with automatic shifting of subsequent content.

## Getting Started

### Installation

**NuGet Package Manager:**

```bash
Install-Package SoundFlow
```

**.NET CLI:**

```bash
dotnet add package SoundFlow
```

### Basic Usage Example

This example demonstrates how to initialize a device and play an audio file using SoundFlow.

```csharp
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Providers;
using SoundFlow.Structs;

// 1. Initialize the Audio Engine context.
// This acts as a manager for all audio devices.
using var engine = new MiniAudioEngine();

// 2. Define the desired audio format.
var format = AudioFormat.Dvd; // 48kHz, 16-bit Stereo

// 3. Initialize a specific playback device. Passing `null` will use the system default too.
var defaultDevice = engine.PlaybackDevices.FirstOrDefault(x => x.IsDefault);
using var playbackDevice = engine.InitializePlaybackDevice(defaultDevice, format);

// 4. Create a SoundPlayer to play an audio file.
var player = new SoundPlayer(engine, device.Format,
    new StreamDataProvider(engine, format, File.OpenRead("path/to/your/audiofile.wav")));

// 5. Add the player to the device's MasterMixer.
// Each device has its own independent mixer.
device.MasterMixer.AddComponent(player);

// 6. Start the device to begin audio processing.
device.Start();
player.Play();

// Keep the console application running.
Console.WriteLine("Playing audio... Press any key to stop.");
Console.ReadKey();

// Stop and clean up.
player.Stop();
device.Stop();
```

## Core Concepts

SoundFlow is built upon a few key concepts:

*   **Audio Engine (`AudioEngine`):** The central context that manages the audio backend and acts as a factory for audio devices. It does not control audio directly but provides the foundation for creating and managing devices.
*   **Audio Devices (`AudioPlaybackDevice`, `AudioCaptureDevice`):** The core of the runtime. These objects represent an initialized piece of hardware (like a sound card or microphone). Each playback device has its own independent `MasterMixer` and audio graph, allowing for multiple, separate audio streams.
*   **Full Duplex Device (`FullDuplexDevice`):** A high-level helper that pairs a playback and capture device, simplifying tasks that require simultaneous input and output, such as live effects monitoring.
*   **Sound Components (`SoundComponent`):** Modular units that process or generate audio, forming a directed graph through input and output connections.
*   **Mixer (`Mixer`):** Combines multiple audio streams into a single output. Each `AudioPlaybackDevice` has its own `MasterMixer` which serves as the root of its audio graph.
*   **Sound Modifiers (`SoundModifier`):** Apply audio effects like reverb, chorus, delay, and equalization.
*   **Audio Playback & Recording:** Components for playing and capturing audio, including surround sound support.
*   **Audio Providers:** Standardized way to read audio data from various sources (files, streams, memory).
*   **Audio Analysis & Visualization:** Tools for extracting information from audio and creating visual representations.
*   **Composition (`Composition`):** The top-level project file that defines a multi-track audio timeline, applies master effects, and manages persistence.
*   **Tracks (`Track`):** Containers within a `Composition` that hold `AudioSegment`s. Tracks have their own settings for volume, pan, mute, solo, and can host effects/analyzers.
*   **Audio Segments (`AudioSegment`):** Individual audio clips placed on a `Track`'s timeline. Segments reference a portion of an `ISoundDataProvider` and can apply segment-specific settings like fades, looping, reversal, speed, and pitch-preserved time stretching.
*   **Time Stretchers (`WsolaTimeStretcher`):** An advanced internal component used by `AudioSegment` and `SoundPlayerBase` to perform high-quality, pitch-preserved time manipulation.

**For detailed information on these concepts, please refer to the [SoundFlow Documentation](https://lsxprime.github.io/soundflow-docs/).**

## Extensions

SoundFlow's architecture supports adding specialized audio processing capabilities via dedicated NuGet packages. These extensions integrate external libraries, making their features available within the SoundFlow ecosystem, often as `SoundModifier`s or utility components.

### SoundFlow.Extensions.WebRtc.Apm

This package provides an integration with a native library based on the **WebRTC Audio Processing Module (APM)**. The WebRTC APM is a high-quality suite of algorithms commonly used in voice communication applications to improve audio quality.

Features included in this extension:

*   **Acoustic Echo Cancellation (AEC):** Reduces echoes caused by playback audio being picked up by the microphone.
*   **Noise Suppression (NS):** Reduces steady-state background noise.
*   **Automatic Gain Control (AGC):** Automatically adjusts the audio signal level to a desired target.
*   **High Pass Filter (HPF):** Removes low-frequency components (like DC offset or rumble).
*   **Pre-Amplifier:** Applies a fixed gain before other processing.

These features are primarily exposed through a single `WebRtcApmModifier` component, which is designed for real-time audio graph processing. A separate `NoiseSuppressor` component is also available for offline/batch processing of audio data from an `ISoundDataProvider`.

**Learn more and find usage examples in the [SoundFlow.Extensions.WebRtc.Apm README](LINK_TO_WEBRTC_APM_README).**

**Note:** The WebRTC APM native library has specific requirements, notably supporting only certain sample rates (8000, 16000, 32000, or 48000 Hz). Ensure your audio devices are initialized with one of these rates when using this extension.

## API Reference

Comprehensive API documentation will be available on the **[SoundFlow Documentation](https://lsxprime.github.io/soundflow-docs/)**.

## Tutorials and Examples

The **[Documentation](https://lsxprime.github.io/soundflow-docs/)** provides a wide range of tutorials and examples to help you get started:

*   **Playback:** Playing audio files and streams, controlling playback.
*   **Recording:** Recording audio, using voice activity detection.
*   **Effects:** Applying various audio effects.
*   **Analysis:** Getting RMS level, analyzing frequency spectrum.
*   **Visualization:** Creating level meters, waveform displays, and spectrum analyzers.
*   **Composition:** Managing audio projects, including creating, editing, and saving multi-track compositions.

**(Note:** You can also find extensive example code in the `Samples` folder of the repository.)

## Contributing

We welcome contributions to SoundFlow! If you'd like to contribute, please follow these guidelines:

1.  **Report Issues:** If you find a bug or have a feature request, please open an issue on the GitHub repository.
2.  **Ask Questions and Seek Help:** If you have questions about using SoundFlow or need help with a specific issue, please open a discussion on the GitHub repository to keep the issues section clean and focused.
3.  **Submit Pull Requests:**
    *   Fork the repository.
    *   Create a new branch for your changes.
    *   Make your changes, following the project's coding style and conventions.
    *   Make sure your changes is well tested.
    *   Submit a pull request to the `master` branch.
4.  **Coding Style:**
    *   Follow the .NET coding conventions.
    *   Use clear and descriptive variable and method names.
    *   Write concise and well-documented code.
    *   Use XML documentation comments for public members.

## Acknowledgments

We sincerely appreciate the foundational work provided by the following projects and modules:

*   **[miniaudio](https://github.com/mackron/miniaudio)** - Provides a lightweight and efficient audio I/O backend.
*   **[WebRTC Audio Processing Module (APM)](https://gitlab.freedesktop.org/pulseaudio/webrtc-audio-processing)** - Offers advanced audio processing (AEC, AGC, Noise Suppression, etc) based on WebRTC's module, thanks to PulseAudio efforts.

## Support This Project

SoundFlow is an open-source project driven by passion and community needs. Maintaining and developing a project of this scale, especially with thorough audio testing, requires significant time and resources.

Currently, development and testing are primarily done using built-in computer speakers. **Your support will directly help improve the quality of SoundFlow by enabling the purchase of dedicated headphones and audio equipment for more accurate and comprehensive testing across different audio setups.**

Beyond equipment, your contributions, no matter the size, help to:

*   **Dedicate more time to development:** Allowing for faster feature implementation, bug fixes, and improvements.
*   **Enhance project quality:** Enabling better testing, documentation, and overall project stability (including better audio testing with proper equipment!).
*   **Sustain long-term maintenance:** Ensuring SoundFlow remains actively maintained and relevant for the community.

You can directly support SoundFlow and help me get essential headphones through:

*   **AirTM:** For simple one-time donations with various payment options like Direct Bank Transfer (ACH), Debit / Credit Card via Moonpay, Stablecoins, and more than 500 banks and e-wallets.

    [Donate using AirTM](https://airtm.me/lsxprime)

*   **Binance Pay (Crypto - Preferred):** Support with cryptocurrency via Binance Pay.

    [Binance Pay QR Code/Link](https://app.binance.com/qr/dplk0837ff4256a64749a2b10dfe3ea5a0b9)

    You can also scan this QR code in your Camera app:

[![photo-2025-02-08-13-36-41.jpg](https://i.postimg.cc/02cL1X8K/photo-2025-02-08-13-36-41.jpg)](https://postimg.cc/9rwxGpwc)

**By becoming a sponsor or making a donation, you directly contribute to the future of SoundFlow and help ensure it sounds great for everyone. Thank you for your generosity!**

## License

SoundFlow is released under the [MIT License](LICENSE.md).

## An Ethical Stance

**While building powerful tools to help make human life better is commendable, we must also acknowledge the horrific injustices taking place in Palestine and across the region.** Israel’s actions since October 7th, 2023 have escalated into a brutal campaign of ethnic cleansing disguised as “defense.” The consequences are devastating, particularly in Gaza, but extend to Lebanon, Syria, and Iran.

This is not a conflict between equal sides; it's a systematic assault by an occupying power on a stateless population struggling for basic human rights.

**The situation in Gaza is catastrophic:**

*   **Massacres of Civilians:** Over 61,200 Palestinians are dead, including tens of thousands of women and children. Israel indiscriminately bombs densely populated areas, targeting civilian shelters as well as hospitals like the Al-Shifa Hospital, which provided crucial healthcare to over a million people in Gaza, obliterating their lifeline and leaving them with no access to essential medical care.
*   **Starvation as Warfare:** Israel's relentless siege blocks vital supplies of food, water, medicine, fuel, and even building materials for repairs, pushing the population towards starvation. Children are dying before aid can reach them. The UN has condemned this as a collective punishment that violates international humanitarian law.
*   **Forced Displacement & Land Confiscation:** Nearly 90% of Gaza’s 2.1 million inhabitants have been displaced multiple times, trapped in overcrowded camps with no end in sight to the siege and relentless bombings. This systematic displacement aims to erase Palestinian identity from the land they rightfully call home, forcing them onto crowded, contaminated, and dangerous territory while Israel continues to confiscate land for its own settlements.
*   **Destruction of Infrastructure:** Israel systematically targets essential infrastructure - hospitals, schools, power stations, water treatment plants – crippling Gaza's ability to function and leaving people without basic necessities like clean water and electricity.

**Syria:**

*   **Israel's Brutal Attacks:** Israel has intensified its brutal attacks on Syrian territory, striking government buildings in Damascus with impunity, escalating tensions with the already fractured nation and claiming territory under a false pretense of defense for the Druze minority facing internal conflict. The world remains silent as they violate Syria's sovereignty.
*   **Tensions between Druze and Bedouin:** Druze factions in Southern Syria have engaged in intense violence against Bedouin tribes, involving killings, forced displacement, and humiliation. Israel explicitly supported these Druze interests by conducting airstrikes in Damascus on July 16, 2025. These strikes were claimed as a "warning" in defense of the Druze amidst their clashes, showcasing Israel's direct military backing in the conflict.

**Lebanon:** Israel’s violation of Lebanon’s airspace and frequent incursions have heightened fears of another devastating war. Targeted killings and bombings are displacing Lebanese civilians, further destabilizing a nation struggling to recover from past conflicts.

**Iran:**

*   **Bombing of Civilians:** Israeli strikes on Iran that began on June 13, 2025, have resulted in significant civilian casualties. According to a US-based human rights group, at least 950 people have been killed, including 380 identified civilians, with over 3,450 others wounded. Iran's Health Ministry has reported that over 90% of the injuries occurred among civilians, including women and children.
*   **A "Crybaby" Reaction to Retaliation:** While inflicting heavy civilian casualties in Iran, Israel has shown a different face when faced with retaliation. After Iran launched missiles and drones in response, killing 29 people and wounding over 3,000 in Israel, Israeli Prime Minister Benjamin Netanyahu called the Iranian regime "weak" and appealed for international support. This reaction highlights a pattern of aggression without accepting the consequences when an opponent can strike back with force.
*   **Begging for US Intervention and Failure to Achieve Goals:** Israel's military campaign, dubbed "Operation Rising Lion," aimed to destroy Iran's nuclear program. However, some of Iran's nuclear facilities are buried deep underground, reportedly beyond the reach of Israeli weapons. This led to Israel seeking and receiving direct military intervention from the United States, which used "bunker buster" bombs on Iranian nuclear sites. Despite the joint effort, assessments indicate that while Iran's nuclear program was set back, it was not eliminated. Reports suggest that some sites are repairable and that Iran retains the materials and knowledge to rebuild, potentially being more motivated to develop nuclear weapons to deter future attacks.

**The situation in Egypt is also not that good**

*   **Tensions between Egypt and Israel:** Tensions between Egypt and Israel have reached their highest level since the war began, primarily due to Israel's policies aimed at forcibly displacing Palestinians toward Egypt's borders. Egypt views this as a direct threat to its national security and rejects any plan for mass displacement into Sinai. Egypt has expressed strong objections to Israel's continued military control over the Philadelphi Corridor and its refusal to reopen border crossings.
*   **Pressure on Borders:** Israel pressures Egypt to accept the flow of Palestinian refugees fleeing the carnage in Gaza, threatening regional stability and exacerbating existing tensions within Sinai. This pressure plays on long-standing historical grievances between Palestinians and Egyptian authorities who see this influx as a destabilizing force under Israeli manipulation.
*   **Threatening Peace Treaties:** By flagrantly violating Egypt's territorial sovereignty through incursions and air strikes, Israel threatens the fragile peace agreement signed in 1979.

**This is not simply a regional conflict; it’s a systematic pattern of violence driven by illegal occupation, apartheid policies, and blatant disregard for international law.**

The international community remains largely complicit, failing to hold Israel accountable for its crimes against humanity and enabling the expansionism that fuels this ongoing catastrophe.

**We urge you as developers to consider:**

*   **Boycotting all products and services that directly benefit the Israeli military machine.** Your money funds their brutal weapons and technology used in Gaza, Syria, Lebanon, and Iran – investing in businesses that profit from human suffering is ethically untenable.
*   **Using your platform and influence to raise awareness about this injustice.** Silence empowers aggressors. Speak out against Israeli policies and war crimes. Demand accountability for every Palestinian life lost under their relentless assault.
*   **Supporting Palestinian-led organizations working on the ground in Palestine, Syria, Lebanon, and Egypt:** They provide vital aid, resist oppression, document human rights abuses, and fight for justice.

**Your skills are powerful tools.** Use them responsibly and ethically. The reality is brutal: a child's body shattered by bombs in Gaza, families torn apart in Lebanon while their homes are reduced to rubble, land stolen while its rightful owners cower under the threat of death.

**Don’t be complicit.** History is not written by the victors alone – stand up for justice against this violent expansionism before it engulfs the entire region.

Learn more about the reality on the ground from reliable sources, not colonizer-backed websites.