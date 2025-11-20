<div align="center">
    <img src="https://raw.githubusercontent.com/LSXPrime/SoundFlow/refs/heads/master/logo.png" alt="Project Logo" width="256" height="256">

# SoundFlow

**A Powerful and Extensible .NET Audio Engine for Enterprise Applications**

[![Build Status](https://github.com/LSXPrime/SoundFlow/actions/workflows/build.yml/badge.svg)](https://github.com/LSXPrime/SoundFlow/actions/workflows/build.yml) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT) [![NuGet](https://img.shields.io/nuget/v/SoundFlow.svg)](https://www.nuget.org/packages/SoundFlow) [![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

</div>

[![Stand With Palestine](https://raw.githubusercontent.com/Safouene1/support-palestine-banner/master/banner-support.svg)](https://thebsd.github.io/StandWithPalestine)
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
*   **Pluggable Codec System:** Extend format support dynamically via `ICodecFactory`. Includes built-in support for WAV, MP3, and FLAC (via MiniAudio), with extensive format support available via extensions.
*   **Robust Metadata Handling:** Read and write metadata tags (ID3v1, ID3v2, Vorbis Comments, MP4 Atoms) and embedded Cue Sheets for a wide range of formats (MP3, FLAC, OGG, M4A, WAV, AIFF).
*   **High Performance:** Optimized for real-time audio processing with SIMD support and efficient memory management.
*   **Playback:** Play audio from various sources, including files, streams, and in-memory assets.
*   **Recording:** Capture audio input and save it to different encoding formats.
*   **Mixing:** Combine multiple audio streams with precise control over volume and panning.
*   **Effects:** Apply a wide range of audio effects, including reverb, chorus, delay, equalization, and more.
*   **Visualization & Analysis:** Create engaging visual representations with FFT-based spectrum analysis, voice activity detection, and level metering.
*   **Surround Sound:** Supports advanced surround sound configurations with customizable speaker positions, delays, and panning methods.
*   **HLS Streaming Support:** Integrate internet radio and online audio via HTTP Live Streaming.
*   **Backend Agnostic:** Supports the `MiniAudio` backend out of the box, with the ability to add others.
*   **Synthesis Engine:**
    *   **Polyphonic Synthesizer:** A robust synthesis engine supporting unison, filtering, and modulation envelopes.
    *   **SoundFont Support:** Native loading and playback of SoundFont 2 (.sf2) banks.
    *   **MPE Support:** Full support for MIDI Polyphonic Expression for per-note control of pitch, timbre, and pressure.
*   **MIDI Ecosystem:**
    *   **Cross-Platform I/O:** Send and receive MIDI messages from hardware devices via the PortMidi backend.
    *   **Routing & Effects:** Graph-based MIDI routing with a suite of modifiers including Arpeggiators, Harmonizers, Randomizers, and Velocity curves.
    *   **Parameter Mapping:** Real-time MIDI mapping system allows controlling any engine parameter (Volume, Filter Cutoff, etc.) via external hardware controllers.
*   **Non-Destructive Audio & MIDI Editing:**
    *   **Compositions & Tracks:** Organize projects into multi-track compositions supporting both Audio and MIDI tracks.
    *   **Hybrid Timeline:** Mix audio clips and MIDI segments on the same timeline.
    *   **Sequencing:** Sample-accurate MIDI sequencing with quantization, swing, and tempo map support.
    *   **Project Persistence:** Save/Load full projects including audio assets, MIDI sequences, tempo maps, and routing configurations.

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

*   **Decoders/Encoders:** Adds support for MP3, AAC, OGG Vorbis, Opus, ALAC, AC3, PCM variations, and many more.
*   **Container Support:** Handles complex containers like M4A, MKA, and others.
*   **Automatic Registration:** simply registering the factory enables the engine to auto-detect and play these formats transparently.

### SoundFlow.Midi.PortMidi

This package provides the backend implementation for MIDI hardware I/O using **PortMidi**.

*   **Hardware Access:** Enumerates and connects to physical MIDI keyboards, synthesizers, and controllers on Windows, macOS, and Linux.
*   **Synchronization:** Provides high-precision clock synchronization, allowing SoundFlow to act as a MIDI Clock Master or Slave.

### SoundFlow.Extensions.WebRtc.Apm

This package provides an integration with a native library based on the **WebRTC Audio Processing Module (APM)**. The WebRTC APM is a high-quality suite of algorithms commonly used in voice communication applications to improve audio quality.

Features included in this extension:

*   **Acoustic Echo Cancellation (AEC):** Reduces echoes caused by playback audio being picked up by the microphone.
*   **Noise Suppression (NS):** Reduces steady-state background noise.
*   **Automatic Gain Control (AGC):** Automatically adjusts the audio signal level to a desired target.
*   **High Pass Filter (HPF):** Removes low-frequency components (like DC offset or rumble).
*   **Pre-Amplifier:** Applies a fixed gain before other processing.

**Note:** The WebRTC APM native library has specific requirements, notably supporting only certain sample rates (8000, 16000, 32000, or 48000 Hz). Ensure your audio devices are initialized with one of these rates when using this extension.

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

**(Note:** You can also find extensive example code in the `Samples` folder of the repository.)

## Contributing

We deeply appreciate your interest in improving SoundFlow.

For detailed guidelines on how to report bugs, suggest features, and submit pull requests, please consult the **[CONTRIBUTING.md](CONTRIBUTING.md)** file for more information.

## Acknowledgments

We sincerely appreciate the foundational work provided by the following projects and modules:

*   **[miniaudio](https://github.com/mackron/miniaudio)** - Provides a lightweight and efficient audio I/O backend.
*   **[FFmpeg](https://ffmpeg.org/)** - The leading multimedia framework, powering our codec extension.
*   **[PortMidi](https://github.com/PortMidi/portmidi)** - Enables cross-platform MIDI I/O.
*   **[WebRTC Audio Processing Module (APM)](https://gitlab.freedesktop.org/pulseaudio/webrtc-audio-processing)** - Offers advanced audio processing (AEC, AGC, Noise Suppression).

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

*   **USDT (Tron/TRC20):** Supporting directly by sending to the following USDT wallet address.

    `TKZzeB71XacY3Av5rnnQVrz2kQqgzrkjFn`

    **Important:** Please ensure you are sending USDT via the **TRC20 (Tron)** network. Sending funds on any other network may result in their permanent loss.

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
*   **Destruction of Infrastructure:** Israel systematically targets essential infrastructure - hospitals, schools, power stations, water treatment plants – crippling Gaza's ability to function and leaving people without necessities like clean water and electricity.

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