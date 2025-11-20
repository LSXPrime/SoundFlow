using System.Text;

namespace SoundFlow.Metadata.Models;

/// <summary>
///     Represents a single track or point within a Cue Sheet.
/// </summary>
public sealed class CuePoint
{
    /// <summary>
    /// Gets the unique identifier for the cue point or track number (e.g., in a CD context).
    /// </summary>
    public uint Id { get; internal set; }
    
    /// <summary>
    /// Gets the position of the cue point in audio samples from the beginning of the file.
    /// </summary>
    public ulong PositionSamples { get; internal set; }
    
    /// <summary>
    /// Gets the label or title associated with the cue point.
    /// </summary>
    public string Label { get; internal set; } = string.Empty;
    
    /// <summary>
    /// Gets the time offset of the cue point, usually calculated from the <see cref="PositionSamples"/>.
    /// </summary>
    public TimeSpan StartTime { get; internal set; }
}

/// <summary>
///     Represents an embedded Cue Sheet with a collection of cue points.
/// </summary>
public sealed class CueSheet
{
    private readonly List<CuePoint> _cuePoints = new();
    
    /// <summary>
    /// Gets a read-only list of the cue points contained in this sheet.
    /// </summary>
    public IReadOnlyList<CuePoint> CuePoints => _cuePoints.AsReadOnly();

    internal void Add(CuePoint point)
    {
        _cuePoints.Add(point);
    }

    internal void Sort()
    {
        _cuePoints.Sort((a, b) => a.PositionSamples.CompareTo(b.PositionSamples));
    }

    /// <summary>
    /// Returns a string representation of the Cue Sheet, listing its points.
    /// </summary>
    /// <returns>A formatted string detailing the cue points.</returns>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var cue in _cuePoints)
            sb.AppendLine($@"  - Track {cue.Id:D2} [{cue.StartTime:hh\:mm\:ss\.fff}]: {cue.Label}");
        return sb.ToString().TrimEnd();
    }
}