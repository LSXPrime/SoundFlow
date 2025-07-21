using System.Numerics;
using SoundFlow.Interfaces;

namespace SoundFlow.Visualization;

/// <summary>
/// Visualizes the current audio level (RMS or peak).
/// </summary>
public class LevelMeterVisualizer : IVisualizer
{
    private readonly LevelMeterAnalyzer _levelMeterAnalyzer;
    private float _level; // Normalized level (0-1)
    private Color _barColor = new(0, 1, 0);
    private Color _peakHoldColor = new(1, 0, 0);
    private float _peakHoldLevel; // Normalized peak hold level (0-1)
    private DateTime _lastPeakTime;
    private const float PeakHoldDuration = 1000; // Milliseconds

    /// <inheritdoc />
    public string Name { get; } = "Level Meter Visualizer";

    /// <summary>
    /// Gets or sets the color of the level bar.
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
    /// Gets or sets the color of the peak hold indicator.
    /// </summary>
    public Color PeakHoldColor
    {
        get => _peakHoldColor;
        set
        {
            _peakHoldColor = value;
            VisualizationUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the size of the Level Meter Visualizer in pixels (X - width, Y - height).
    /// </summary>
    public static Vector2 Size => new(20, 200);

    /// <summary>
    /// Initializes a new instance of the <see cref="LevelMeterVisualizer"/> class.
    /// </summary>
    /// <param name="levelMeterAnalyzer">The level meter analyzer to get data from.</param>
    public LevelMeterVisualizer(LevelMeterAnalyzer levelMeterAnalyzer)
    {
        _levelMeterAnalyzer = levelMeterAnalyzer;
        _lastPeakTime = DateTime.MinValue;
    }

    /// <inheritdoc/>
    public void ProcessOnAudioData(Span<float> audioData)
    {
        _level = _levelMeterAnalyzer.Rms;

        // Update peak hold
        if (_level > _peakHoldLevel)
        {
            _peakHoldLevel = _level;
            _lastPeakTime = DateTime.Now;
        }
        else if ((DateTime.Now - _lastPeakTime).TotalMilliseconds > PeakHoldDuration)
        {
            _peakHoldLevel = _level; // Decay peak hold
        }

        VisualizationUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Render(IVisualizationContext context)
    {
        context.Clear();

        // Draw level bar
        var levelHeight = _level * Size.Y;
        context.DrawRectangle(0, Size.Y - levelHeight, Size.X, levelHeight, _barColor);

        // Draw peak hold indicator
        var peakHoldY = Size.Y - _peakHoldLevel * Size.Y;
        context.DrawLine(0, peakHoldY, Size.X, peakHoldY, _peakHoldColor);
    }

    /// <inheritdoc />
    public event EventHandler? VisualizationUpdated;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
