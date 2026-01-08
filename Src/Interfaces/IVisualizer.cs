namespace SoundFlow.Interfaces;

/// <summary>
/// Represents a visualizer that can process audio data and generate visualization data.
/// </summary>
public interface IVisualizer : IDisposable
{
    /// <summary>
    /// Gets the name of the visualizer.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Processes the audio data. This method is called by an analyzer component.
    /// </summary>
    /// <param name="audioData">The audio data to process.</param>
    void ProcessOnAudioData(ReadOnlySpan<float> audioData);

    /// <summary>
    /// Updates the visualization. This method should be called periodically to render the visualization.
    /// </summary>
    /// <param name="context">An object providing methods to draw the visualization (defined later).</param>
    void Render(IVisualizationContext context);

    /// <summary>
    /// Raised when the visualization data needs to be redrawn.
    /// </summary>
    event EventHandler VisualizationUpdated;
}

/// <summary>
/// Represents a color.
/// </summary>
public readonly struct Color(float r, float g, float b, float a = 1f)
{
    /// <summary>
    /// The red component (0-1).
    /// </summary>
    public readonly float R = r;

    /// <summary>
    /// The green component (0-1).
    /// </summary>
    public readonly float G = g;

    /// <summary>
    /// The blue component (0-1).
    /// </summary>
    public readonly float B = b;

    /// <summary>
    /// The alpha component (0-1).
    /// </summary>
    public readonly float A = a;
}
