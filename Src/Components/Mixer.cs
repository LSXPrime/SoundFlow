using System.Collections.Concurrent;
using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Structs;

namespace SoundFlow.Components;

/// <summary>
///     Represents an audio mixer that combines and processes audio from multiple SoundComponents.
/// </summary>
public sealed class Mixer : SoundComponent
{
    private readonly ConcurrentDictionary<SoundComponent, byte> _components = new();
    
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
                return _components.Keys.ToArray();
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

            if (_components.TryAdd(component, 0))
                component.Parent = this;
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
            if (_components.TryRemove(component, out _))
                component.Parent = null;
        }
    }

    /// <inheritdoc />
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        if (!Enabled || Mute || _isDisposed)
            return;

        lock (_modificationLock)
        {
            foreach (var component in _components.Keys)
            { 
                if (component is { Enabled: true, Mute: false }) 
                    component.Process(buffer, channels);

            }
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
            foreach (var component in _components.Keys)
            {
                component.Parent = null;
                if (component is IDisposable disposable)
                    disposable.Dispose();
            }
            _components.Clear();
        }
        
        base.Dispose();
    }
}
