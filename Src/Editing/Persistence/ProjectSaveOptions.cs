namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Provides a set of configurable options for saving a composition project.
/// </summary>
public class ProjectSaveOptions
{
    /// <summary>
    /// The project file version to write into the saved file. 
    /// It is recommended to use the library's default version unless you have a specific need for version management.
    /// The current default is "1.3.0".
    /// </summary>
    public string ProjectFileVersion { get; set; } = "1.3.0";

    /// <summary>
    /// The name of the subfolder where consolidated media files will be stored, relative to the project file.
    /// Default is "Assets".
    /// </summary>
    public string ConsolidatedMediaFolderName { get; set; } = "Assets";

    /// <summary>
    /// The maximum size in bytes for a media source to be embedded directly into the project file as Base64 data.
    /// Sources larger than this will be consolidated to a file instead (if consolidation is enabled).
    /// Default is 1MB (1 * 1024 * 1024 bytes).
    /// </summary>
    public long MaxEmbedSizeBytes { get; set; } = 1 * 1024 * 1024;

    /// <summary>
    /// If true, external and in-memory/stream audio sources will be copied into the 'ConsolidatedMediaFolderName'
    /// to make the project self-contained.
    /// Default is true.
    /// </summary>
    public bool ConsolidateMedia { get; set; } = true;

    /// <summary>
    /// If true, small audio sources (below 'MaxEmbedSizeBytes') will be embedded directly into the project file,
    /// ignoring the 'ConsolidateMedia' setting for those specific sources.
    /// Default is true.
    /// </summary>
    public bool EmbedSmallMedia { get; set; } = true;
}