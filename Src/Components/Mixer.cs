using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Structs;

namespace SoundFlow.Components;

/// <summary>
///     Represents an audio mixer that combines and processes audio from multiple SoundComponents.
/// </summary>
public sealed class Mixer : SoundComponent
{
    // Staging list for thread-safe modification
    private readonly List<SoundComponent> _componentsStaging = [];
    
    // Snapshot for the audio thread to read without locking
    private volatile SoundComponent[] _componentsSnapshot = [];

    private readonly object _modificationLock = new();
    private volatile bool _isDisposed;

    /// <summary>
    /// Gets the playback device this mixer is the master for, if any.
    /// </summary>
    public AudioPlaybackDevice? ParentDevice { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether this is a master mixer for a device.
    /// </summary>
    public bool IsMasterMixer { get; }
    
    /// <summary>
    /// Gets the list of sound components in the mixer.
    /// </summary>
    public IReadOnlyCollection<SoundComponent> Components
    {
        get
        {
            lock (_modificationLock)
            {
                return new List<SoundComponent>(_componentsStaging);
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Mixer"/> class.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    /// <param name="isMasterMixer">Indicates if this is a master mixer for a device.</param>
    public Mixer(AudioEngine engine, AudioFormat format, bool isMasterMixer = false) : base(engine, format)
    {
        IsMasterMixer = isMasterMixer;
        Name = isMasterMixer ? "Master Mixer" : "Mixer";
    }

    /// <inheritdoc />
    public override string Name { get; set; }

    /// <summary>
    ///     Adds a sound component to the mixer.
    /// </summary>
    /// <param name="component">The sound component to add.</param>
    /// <exception cref="ArgumentException">
    ///     Thrown if the component is the mixer itself or if adding the component would create
    ///     a cycle in the graph.
    /// </exception>
    /// <exception cref="ObjectDisposedException">Thrown if the mixer has been disposed.</exception>
    public void AddComponent(SoundComponent component)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(component);

        lock (_modificationLock)
        {
            if (WouldCreateCycle(component))
                throw new ArgumentException("Adding this component would create a cycle in the audio graph.",
                    nameof(component));

            if (_componentsStaging.Contains(component)) return;
            _componentsStaging.Add(component);
            component.Parent = this;
            _componentsSnapshot = _componentsStaging.ToArray();
        }
    }

    /// <summary>
    ///     Checks if adding a component to this mixer would create a cycle in the audio graph.
    /// </summary>
    /// <param name="component">The component to check.</param>
    /// <returns>True if adding the component would create a cycle, false otherwise.</returns>
    private bool WouldCreateCycle(SoundComponent component)
    {
        var current = Parent;
        while (current != null)
        {
            if (current == component)
                return true;

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    ///     Removes a sound component from the mixer.
    /// </summary>
    /// <param name="component">The sound component to remove.</param>
    public void RemoveComponent(SoundComponent component)
    {
        if (_isDisposed || component == null!)
            return;

        lock (_modificationLock)
        {
            if (!_componentsStaging.Remove(component)) return;
            component.Parent = null;
            _componentsSnapshot = _componentsStaging.ToArray();
        }
    }

    /// <inheritdoc />
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        if (!Enabled || Mute || _isDisposed)
            return;

        // Read reference to the snapshot array.
        var components = _componentsSnapshot;

        foreach (var component in components)
        { 
            if (component is { Enabled: true, Mute: false }) 
                component.Process(buffer, channels);
        }
    }

    /// <summary>
    ///     Disposes the mixer and all its components.
    /// </summary>
    /// <remarks>
    ///     After disposal, the mixer cannot be used anymore.
    ///     All components will be removed and disposable components will be disposed.
    /// </remarks>
    public override void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        
        lock (_modificationLock)
        {
            foreach (var component in _componentsStaging)
            {
                component.Parent = null;
                if (component is IDisposable disposable)
                    disposable.Dispose();
            }
            _componentsStaging.Clear();
            _componentsSnapshot = [];
        }
        
        base.Dispose();
    }
}