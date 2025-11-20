# Contributing to SoundFlow

First off, thank you for considering contributing to SoundFlow! It's people like you that make SoundFlow such a powerful tool for the .NET community.

SoundFlow is an open-source project driven by passion. To give you an idea of the dedication invested: **As of the end of 2025, SoundFlow already represents over 8,000 hours of dedicated development effort.** This commitment is enduring—the development will continue indefinitely, as long as I am alive and someone is still actively using the project.

We welcome contributions from everyone, whether it's reporting a bug, improving documentation, proposing new features, or writing code.

## Table of Contents

1.  [Code of Conduct & Ethical Standards](#code-of-conduct--ethical-standards)
2.  [How Can I Contribute?](#how-can-i-contribute)
    *   [Reporting Bugs](#reporting-bugs)
    *   [Suggesting Enhancements](#suggesting-enhancements)
    *   [Your First Code Contribution](#your-first-code-contribution)
3.  [Development Guidelines](#development-guidelines)
    *   [Environment Setup](#environment-setup)
    *   [Project Structure](#project-structure)
    *   [Coding Standards](#coding-standards)
    *   [Performance Considerations](#performance-considerations)
4.  [Pull Request Process](#pull-request-process)
5.  [Documentation](#documentation)

---

## Code of Conduct & Ethical Standards

### Mutual Respect
We expect all contributors to treat each other with respect and professional courtesy. Harassment, hate speech, and abusive behavior will not be tolerated.

### Ethical Alignment
As stated in our README, SoundFlow maintains a firm **[Ethical Stance](README.md#an-ethical-stance)** regarding human rights and the ongoing situation in Palestine and the surrounding region.

By contributing to this project, you acknowledge that:
1.  **No Harm:** We will not accept code contributions designed specifically to facilitate military operations, mass surveillance, or oppression.
2.  **Openness:** This project is intended to empower developers to build creative, communicative, and constructive tools.

---

## How Can I Contribute?

### Reporting Bugs

This section guides you through submitting a bug report for SoundFlow. Following these guidelines helps maintainers and the community understand your report, reproduce the behavior, and find related reports.

*   **Check existing issues:** Search to see if the problem has already been reported.
*   **Use the template:** When opening a new issue, please provide:
    *   **OS & Architecture:** (e.g., Windows 11 x64, Ubuntu 22.04 ARM64, Android 13).
    *   **SoundFlow Version:** (e.g., NuGet v1.3.0 or specific commit hash).
    *   **Audio Device Info:** (e.g., "Realtek High Definition Audio" or "Focusrite Scarlett 2i2").
    *   **Reproduction Steps:** A minimal code snippet that triggers the crash or bug.
    *   **Stack Trace:** If a crash occurs, please provide the full stack trace.

### Suggesting Enhancements

This section guides you through submitting an enhancement suggestion for SoundFlow, including completely new features and minor improvements to existing functionality.

*   **Check the Roadmap:** Look at the Issues tab and Discussions to see what is already planned.
*   **Describe the Goal:** Focus on *what* you want to achieve and *why*, rather than just *how* to implement it.
*   **Context:** Explain why this enhancement would be useful to enterprise applications or general audio developers.

### Your First Code Contribution

Unsure where to begin? You can start by looking through these issues:
*   **Good First Issue:** Issues which should only require a few lines of code, and a test or two.
*   **Documentation:** Improving comments, fixing outdated examples in the documentation website, or adding examples to the `Samples` folder, I prefer to avoid README modifications and small commits to non-code parts to keep the commit log compact.

---

## Development Guidelines

### Environment Setup

To develop SoundFlow, you need:
1.  **The .NET 8.0 SDK** installed.
2.  An IDE (Visual Studio 2022, JetBrains Rider, or VS Code).
3.  **Native Dependencies:**
    *   The project relies on `miniaudio` (and optionally `ffmpeg`/`portmidi/webrtc-apm`).
    *   Ensure you have the C/C++ build tools installed if you plan on modifying the native interop layers, though usually, the pre-compiled runtimes in `Src/Backends/MiniAudio/runtimes` handle this for standard development.

### Project Structure

*   `Src/` - The core library code.
    *   `Abstracts/` - Base classes (`AudioEngine`, `SoundComponent`).
    *   `Backends/` - Implementations for audio I/O (currently MiniAudio).
    *   `Components/` - Audio graph nodes (Mixers, Players, Recorders).
    *   `Editing/` - DAW-like features (Tracks, Segments, Persistence).
    *   `Metadata/` - Tag parsing and writing logic.
    *   `Midi/` - MIDI handling, routing, and logic.
    *   `Synthesis/` - Generators, Voices, and Instrument Banks.
    *   `Utils/` - SIMD math helpers, logging, and extensions.
*   `Samples/` - Example projects demonstrating usage patterns.

### Coding Standards

We adhere to standard C# coding conventions.

1.  **Naming:** PascalCase for public members, _camelCase for private fields.
2.  **Nullability:** Enable Nullable Reference Types (`<Nullable>enable</Nullable>`). Handle null warnings explicitly.
3.  **Documentation:** **All** public classes, methods, and properties **must** have XML documentation comments (`/// <summary>`). As this helps the IDE features (e.g. IntelliSense).
4.  **Formatting:** Keep on source formatting and follow it if possible.

### Performance Considerations

SoundFlow is a high-performance audio engine. When contributing code that runs in the audio thread (e.g., `GenerateAudio`, `Process`, `Mix`), adhere to these strict rules:

1.  **Zero Allocation:** **Never** allocate memory (`new`, `boxing`) inside the audio processing loop. This causes Garbage Collection (GC) pauses, resulting in audio glitches.
2.  **Use Spans:** Use `Span<T>` and `ReadOnlySpan<T>` for buffer manipulation to avoid array copying.
3.  **ArrayPool:** If you need temporary buffers, use `ArrayPool<float>.Shared.Rent()` and `Return()`.
4.  **SIMD:** Utilize `SoundFlow.Utils.MathHelper` or `System.Runtime.Intrinsics` for heavy math operations.
5.  **Unsafe Code:** We use `unsafe` blocks for pointer arithmetic with native audio buffers. Be extremely careful with bounds checking.

---

## Pull Request Process

1.  **Fork the repo** and create your branch from `master`.
2.  **Test:** If you add code, add tests or a sample in the `Samples` folder to demonstrate functionality.
    *   *Note: Because this is an audio engine, programmatic unit tests are hard for some features. "Ear-tests" via a sample application are acceptable for playback logic.*
3.  **Document:** Ensure any new features are documented in the XML comments.
4.  **Format:** Ensure your code is formatted correctly.
5.  **Title:** Make sure your PR title is descriptive (e.g., "Fix clipping in Mixer component" rather than "Fix bug").
6.  **Description:** Link the PR to the issue it solves (e.g., "Closes #123").
7.  **Review:** Be open to feedback. We might ask for changes to align with the architecture or performance requirements.

---

## Documentation

Documentation is vital for an enterprise-grade framework.

*   **XML Comments:** As mentioned, these are non-negotiable for public APIs.
*   **Samples:** If you introduce a complex new feature (like a new Synthesis method), please provide a small Console App or snippet in the `Samples` directory showing how to use it.

---

## A Note on Hardware

As mentioned in the README, development and testing are currently constrained by limited audio hardware. If your contribution involves complex features like **7.1 Surround Sound** or **Hardware-specific MIDI timing**, please be patient with us during the review process, as we may not have the physical hardware to verify it immediately.

If you are able to verify a feature on specific hardware (e.g., a specific audio interface or MIDI controller), please state that in your Pull Request description.

Thank you for contributing to SoundFlow! 🎵