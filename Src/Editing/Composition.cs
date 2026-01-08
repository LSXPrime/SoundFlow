using System.Diagnostics.CodeAnalysis;
using SoundFlow.Abstracts;
using SoundFlow.Editing.Mapping;
using SoundFlow.Interfaces;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Routing.Nodes;
using SoundFlow.Structs;
using SoundFlow.Synthesis.Interfaces;

namespace SoundFlow.Editing;

/// <summary>
/// Represents a complete audio composition, acting as the top-level container for multiple tracks.
/// It serves as a façade, providing access to its data model and specialized services for rendering, editing, and recording.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicMethods)]
public sealed class Composition : ISequencerContext, IDisposable, IMidiMappable
{
    private readonly Dictionary<Guid, IMidiMappable> _objectRegistry = new();
    
    private string _name;
    private float _masterVolume = 1.0f;
    private int _sampleRate;
    private int _targetChannels;
    private bool _isDirty;
    private int _ticksPerQuarterNote;

    /// <inheritdoc />
    public Guid Id { get; } = Guid.NewGuid();
    
    /// <summary>
    /// Explicitly implements the ISequencerContext's read-only TempoTrack property.
    /// </summary>
    IReadOnlyList<TempoMarker> ISequencerContext.TempoTrack => TempoTrack;

    /// <summary>
    /// Gets the rendering service for this composition, which can generate audio output
    /// and acts as an <see cref="ISoundDataProvider"/>.
    /// </summary>
    public CompositionRenderer Renderer { get; }

    /// <summary>
    /// Gets the editing service for this composition, providing methods to manipulate
    /// tracks, segments, and other structural elements.
    /// </summary>
    public CompositionEditor Editor { get; }

    /// <summary>
    /// Gets the recording service for this composition, which manages the MIDI recording workflow.
    /// </summary>
    public CompositionRecorder Recorder { get; }

    /// <summary>
    /// Gets the MIDI mapping service for this composition, which manages real-time control mappings.
    /// </summary>
    public MidiMappingManager MappingManager { get; }

    /// <summary>
    /// Gets the audio format of the composition.
    /// </summary>
    public AudioFormat Format { get; }

    /// <summary>
    /// Gets or sets the name of the composition.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets the chain of sound modifiers (effects) to be applied to the master output of this composition.
    /// These are applied after all tracks are mixed together.
    /// </summary>
    public List<SoundModifier> Modifiers { get; init; } = [];

    /// <summary>
    /// Gets the chain of audio analyzers to process the master output of this composition.
    /// Analyzers process the audio after all master modifiers have been applied.
    /// </summary>
    public List<AudioAnalyzer> Analyzers { get; init; } = [];

    /// <summary>
    /// Gets the list of internal instrument/effect components (e.g., Synthesizers) that can be targeted by MIDI tracks.
    /// These are wrapped in <see cref="MidiTargetNode"/> to conform to the MIDI routing graph.
    /// </summary>
    public List<IMidiDestinationNode> MidiTargets { get; } = [];

    /// <summary>
    /// Gets the list of <see cref="Track"/>s contained within this composition.
    /// </summary>
    public List<Track> Tracks { get; } = [];

    /// <summary>
    /// Gets the list of <see cref="MidiTrack"/>s contained within this composition.
    /// </summary>
    public List<MidiTrack> MidiTracks { get; } = [];

    /// <summary>
    /// Gets the master tempo track for the composition, defining tempo changes over time.
    /// The track is guaranteed to have at least one tempo marker at time zero.
    /// </summary>
    public List<TempoMarker> TempoTrack { get; } = [];

    /// <summary>
    /// Gets or sets the master volume level for the entire composition.
    /// A value of 1.0f is normal volume. Values greater than 1.0f can lead to clipping.
    /// </summary>
    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            if (!(Math.Abs(_masterVolume - value) > 0.0001f)) return;
            _masterVolume = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the composition has unsaved changes.
    /// This flag is set to true when modifications are made and reset after saving.
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Gets or sets the target sample rate for rendering this composition.
    /// This defines the output sample rate when the composition is rendered or read as an <see cref="ISoundDataProvider"/>.
    /// </summary>
    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            if (_sampleRate == value) return;
            _sampleRate = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets or sets the time division for all MIDI tracks in this composition, in ticks per quarter note.
    /// Default is 480.
    /// </summary>
    public int TicksPerQuarterNote
    {
        get => _ticksPerQuarterNote;
        set
        {
            if (_ticksPerQuarterNote == value) return;
            _ticksPerQuarterNote = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the composition has been disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets or sets the target number of channels for rendering this composition.
    /// This defines the output channel count when the composition is rendered or read as an <see cref="ISoundDataProvider"/>.
    /// </summary>
    public int TargetChannels
    {
        get => _targetChannels;
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "Channels must be greater than 0.");
            if (_targetChannels == value) return;
            _targetChannels = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Composition"/> class.
    /// </summary>
    /// <param name="engine">The audio engine instance, required for services like recording and file loading.</param>
    /// <param name="format">The audio format of the composition. Cannot be null.</param>
    /// <param name="name">The name of the composition. Defaults to "Composition".</param>
    /// <param name="targetChannels">Optional target number of channels for the composition's output. If null, uses the format's channel count.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="targetChannels"/> is less than or equal to 0.</exception>
    public Composition(AudioEngine engine, AudioFormat format, string name = "Composition", int? targetChannels = null)
    {
        Format = format;
        _name = name;
        _sampleRate = format.SampleRate;
        if (targetChannels is <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetChannels), "Channels must be greater than 0.");
        _targetChannels = targetChannels ?? format.Channels;
        TicksPerQuarterNote = 480; // Set default TPQN

        // Instantiate the service classes, passing a reference to this composition.
        Renderer = new CompositionRenderer(this);
        Editor = new CompositionEditor(this, engine);
        Recorder = new CompositionRecorder(this);
        MappingManager = new MidiMappingManager(this);

        // Register self in the mappable object registry
        RegisterMappableObject(this);

        // Ensure there is a default tempo marker.
        TempoTrack.Add(new TempoMarker(TimeSpan.Zero, 120.0));
    }

    /// <summary>
    /// Registers a mappable object with the composition's central registry.
    /// </summary>
    /// <param name="obj">The object to register.</param>
    internal void RegisterMappableObject(IMidiMappable obj) => _objectRegistry[obj.Id] = obj;

    /// <summary>
    /// Unregisters a mappable object from the composition's central registry.
    /// </summary>
    /// <param name="obj">The object to unregister.</param>
    internal void UnregisterMappableObject(IMidiMappable obj) => _objectRegistry.Remove(obj.Id);

    /// <summary>
    /// Attempts to retrieve a registered mappable object by its unique ID.
    /// </summary>
    /// <param name="id">The unique ID of the object.</param>
    /// <param name="obj">When this method returns, contains the object if found; otherwise, null.</param>
    /// <returns>True if the object was found; otherwise, false.</returns>
    internal bool TryGetMappableObject(Guid id, out IMidiMappable? obj) => _objectRegistry.TryGetValue(id, out obj);

    /// <summary>
    /// Marks the composition as dirty (having unsaved changes).
    /// </summary>
    public void MarkDirty()
    {
        _isDirty = true;
    }

    /// <summary>
    /// Clears the dirty flag, typically after a successful save operation.
    /// </summary>
    internal void ClearDirtyFlag()
    {
        _isDirty = false;
    }

    /// <summary>
    /// Disposes of all disposable resources owned by the composition and its services.
    /// This includes MIDI recorders and any segments that own their data providers.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Recorder.Dispose();
        Renderer.Dispose();
        Editor.Dispose();
        MappingManager.Dispose();
    }
}