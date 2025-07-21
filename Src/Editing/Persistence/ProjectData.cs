using SoundFlow.Abstracts;

namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Represents the root data structure for serializing an entire <see cref="Composition"/> project.
/// This object encapsulates all necessary information to save and load a project,
/// including composition-wide settings, references to audio sources, and the arrangement of tracks.
/// </summary>
public class ProjectData
{
    /// <summary>
    /// Gets or sets the version of the project file format.
    /// This is used for backward and forward compatibility checks during loading.
    /// </summary>
    public string ProjectFileVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the name of the composition.
    /// </summary>
    public string Name { get; set; } = "Composition";

    /// <summary>
    /// Gets or sets the master volume level for the entire composition.
    /// A value of 1.0f is normal volume.
    /// </summary>
    public float MasterVolume { get; set; } = 1.0f;

    /// <summary>
    /// Gets or sets the target sample rate for rendering this composition.
    /// </summary>
    public int TargetSampleRate { get; set; }

    /// <summary>
    /// Gets or sets the target number of channels for rendering this composition.
    /// </summary>
    public int TargetChannels { get; set; }

    /// <summary>
    /// Gets or sets the list of audio source references used in the composition.
    /// This list allows for sharing of audio source data between multiple segments
    /// and enables media management features such as consolidation and relinking.
    /// </summary>
    public List<ProjectSourceReference> SourceReferences { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of tracks included in the composition.
    /// Each track contains a sequence of audio segments that contribute to the final mix.
    /// </summary>
    public List<ProjectTrack> Tracks { get; set; } = [];
    
    /// <summary>
    /// Gets or sets the list of <see cref="ProjectEffectData"/> instances representing
    /// sound modifiers <see cref="SoundModifier"/> applied to the master composition.
    /// </summary>
    public List<ProjectEffectData> Modifiers { get; set; } = [];
    
    /// <summary>
    /// Gets or sets the list of <see cref="ProjectEffectData"/> instances representing
    /// sound analyzers <see cref="AudioAnalyzer"/> applied to the master composition.
    /// </summary>
    public List<ProjectEffectData> Analyzers { get; set; } = [];

}
