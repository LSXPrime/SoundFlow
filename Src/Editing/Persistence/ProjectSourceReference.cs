using System.Text.Json.Serialization;
using SoundFlow.Enums;
using SoundFlow.Interfaces;

namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Represents how an audio source is referenced within a saved project.
/// </summary>
public class ProjectSourceReference
{
    /// <summary>
    /// A unique identifier for this source within the project.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The original absolute file path of the audio source.
    /// This is stored for reference and potential relinking.
    /// </summary>
    public string? OriginalAbsolutePath { get; set; }

    /// <summary>
    /// The relative path to the audio file if it's consolidated within the project folder.
    /// Null if not consolidated or if embedded.
    /// </summary>
    public string? ConsolidatedRelativePath { get; set; }

    /// <summary>
    /// If true, the audio data for this source is embedded directly in the project file.
    /// </summary>
    public bool IsEmbedded { get; set; }

    /// <summary>
    /// The raw audio data, if IsEmbedded is true. Stored as base64 string for JSON compatibility.
    /// This will be null if the data is not embedded.
    /// </summary>
    public string? EmbeddedDataB64 { get; set; }

    /// <summary>
    /// The original sample format of the source if known, especially important for embedded data.
    /// </summary>
    public SampleFormat OriginalSampleFormat { get; set; } = SampleFormat.F32;

    /// <summary>
    /// The original sample rate of the source if known.
    /// </summary>
    public int? OriginalSampleRate { get; set; }

    /// <summary>
    /// For internal use during loading: flag indicating if the media is currently missing.
    /// Not serialized.
    /// </summary>
    [JsonIgnore]
    public bool IsMissing { get; set; }

    /// <summary>
    /// For internal use during loading: the resolved ISoundDataProvider instance.
    /// Not serialized.
    /// </summary>
    [JsonIgnore]
    public ISoundDataProvider? ResolvedDataProvider { get; set; }
}
