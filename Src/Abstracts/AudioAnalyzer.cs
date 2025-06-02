using SoundFlow.Interfaces;

namespace SoundFlow.Abstracts;

/// <summary>
/// Base class for audio analyzer components that extract data for visualizers.
/// </summary>
public abstract class AudioAnalyzer
{
    /// <summary>
    /// Gets or sets the name of the analyzer.
    /// </summary>
    public virtual string Name { get; set; } = "Audio Analyzer";

    
    /// <summary>
    /// Whether the analyzer is enabled or not.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    private readonly IVisualizer? _visualizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioAnalyzer"/> class.
    /// </summary>
    /// <param name="visualizer">The visualizer to send data to.</param>
    protected AudioAnalyzer(IVisualizer? visualizer = null)
    {
        _visualizer = visualizer;
    }

    
    /// <summary>
    /// Processes the audio data and sends it to the visualizer.
    /// </summary>
    public void Process(Span<float> buffer)
    {
        if (!Enabled) return;
        
        // Perform analysis on the buffer.
        Analyze(buffer);

        // Send data to the visualizer.
        _visualizer?.ProcessOnAudioData(buffer);
    }

    /// <summary>
    /// Analyzes the audio data.
    /// </summary>
    /// <param name="buffer">The audio buffer.</param>
    protected abstract void Analyze(Span<float> buffer);
}