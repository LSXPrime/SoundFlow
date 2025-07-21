namespace SoundFlow.Backends.MiniAudio.Enums;

/// <summary>
/// Defines usage scenarios for WASAPI devices on Windows, affecting system-level prioritization and processing.
/// </summary>
public enum WasapiUsage
{
    /// <summary>
    /// Default usage scenario, balanced for general audio tasks.
    /// </summary>
    Default = 0,
    /// <summary>
    /// Optimized for games, often balancing latency and background audio behavior.
    /// </summary>
    Games,
    /// <summary>
    /// Optimized for professional audio applications requiring low latency and high fidelity.
    /// </summary>
    ProAudio,
}