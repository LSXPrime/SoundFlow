namespace SoundFlow.Editing;

/// <summary>
/// Represents a single, editable automation point for a MIDI parameter like Pitch Bend or a Control Change (CC) message.
/// </summary>
public sealed class ControlPoint
{
    /// <summary>
    /// Gets a unique identifier for this control point instance.
    /// This is used to reliably track the point during editing operations.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the absolute time of the control point in MIDI ticks from the beginning of the sequence.
    /// </summary>
    public long Tick { get; set; }

    /// <summary>
    /// Gets or sets the value of the control point.
    /// For CC messages, this is 0-127.
    /// For Pitch Bend, this is a 14-bit value from 0-16383 (centered at 8192).
    /// </summary>
    public int Value { get; set; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="ControlPoint"/> class.
    /// </summary>
    /// <param name="tick">The absolute time in ticks.</param>
    /// <param name="value">The MIDI value for the control point.</param>
    public ControlPoint(long tick, int value)
    {
        Tick = tick;
        Value = value;
    }
}


/// <summary>
/// A data structure describing a modification to be applied to a <see cref="ControlPoint"/>.
/// Null properties indicate no change for that attribute.
/// </summary>
public record struct ControlPointModification
{
    /// <summary>
    /// The unique identifier of the control point to modify.
    /// </summary>
    public Guid PointId { get; init; }

    /// <summary>
    /// The new absolute time in ticks, if it is to be changed.
    /// </summary>
    public long? NewTick { get; init; }

    /// <summary>
    /// The new value, if it is to be changed.
    /// </summary>
    public int? NewValue { get; init; }
}