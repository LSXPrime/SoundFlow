namespace SoundFlow.Interfaces;

/// <summary>
/// Provides methods for drawing visualization elements.
/// This interface abstracts the underlying graphics system.
/// </summary>
public interface IVisualizationContext
{
    /// <summary>
    /// Clears the drawing area.
    /// </summary>
    void Clear();

    /// <summary>
    /// Draws a line.
    /// </summary>
    /// <param name="x1">The x-coordinate of the starting point.</param>
    /// <param name="y1">The y-coordinate of the starting point.</param>
    /// <param name="x2">The x-coordinate of the ending point.</param>
    /// <param name="y2">The y-coordinate of the ending point.</param>
    /// <param name="color">The color of the line.</param>
    /// <param name="thickness">The thickness of the line.</param>
    void DrawLine(float x1, float y1, float x2, float y2, Color color, float thickness = 1f);

    /// <summary>
    /// Draws a rectangle.
    /// </summary>
    /// <param name="x">The x-coordinate of the top-left corner.</param>
    /// <param name="y">The y-coordinate of the top-left corner.</param>
    /// <param name="width">The width of the rectangle.</param>
    /// <param name="height">The height of the rectangle.</param>
    /// <param name="color">The color of the rectangle.</param>
    void DrawRectangle(float x, float y, float width, float height, Color color);
}
