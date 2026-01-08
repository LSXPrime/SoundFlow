using System.Numerics;
using SoundFlow.Interfaces;

namespace SoundFlow.Visualization;

/// <summary>
/// Visualizes the frequency spectrum of the audio data.
/// </summary>
public class SpectrumVisualizer : IVisualizer
{
    private readonly SpectrumAnalyzer _spectrumAnalyzer;
    private Color _barColor = new(0, 1, 0);

    /// <inheritdoc />
    public string Name { get; } = "Spectrum Visualizer";

    /// <summary>
    /// Gets or sets the color of the spectrum bars.
    /// </summary>
    public Color BarColor
    {
        get => _barColor;
        set
        {
            _barColor = value;
            VisualizationUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the size of the spectrum visualizer in pixels (X - width, Y - height).
    /// </summary>
    public static Vector2 Size => new(800, 200);

    /// <summary>
    /// Initializes a new instance of the <see cref="SpectrumVisualizer"/> class.
    /// </summary>
    /// <param name="spectrumAnalyzer">The spectrum analyzer to get data from.</param>
    public SpectrumVisualizer(SpectrumAnalyzer spectrumAnalyzer)
    {
        _spectrumAnalyzer = spectrumAnalyzer;
    }

    /// <inheritdoc/>
    public void ProcessOnAudioData(ReadOnlySpan<float> audioData)
    {
        // No need to do anything here, the spectrum analyzer already has the data.
        VisualizationUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Render(IVisualizationContext context)
    {
        context.Clear();

        var spectrumData = _spectrumAnalyzer.SpectrumData;
        if (spectrumData.Length == 0)
        {
            return;
        }

        var barWidth = Size.X / spectrumData.Length;

        for (var i = 0; i < spectrumData.Length; i++)
        {
            var barHeight = spectrumData[i] * Size.Y;
            var x = i * barWidth;
            var y = Size.Y - barHeight;

            context.DrawRectangle(x, y, barWidth, barHeight, _barColor);
        }
    }

    /// <inheritdoc />
    public event EventHandler? VisualizationUpdated;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
