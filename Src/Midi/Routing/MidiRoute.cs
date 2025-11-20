using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Routing.Nodes;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Midi.Routing;

/// <summary>
/// Represents a single, end-to-end connection from a MIDI source to a MIDI destination,
/// with an ordered chain of MIDI processors (modifiers).
/// </summary>
public sealed class MidiRoute
{
    private readonly object _lock = new();

    /// <summary>
    /// Gets the source node of this route.
    /// </summary>
    public IMidiSourceNode Source { get; }

    /// <summary>
    /// Gets the destination node of this route.
    /// </summary>
    public IMidiDestinationNode Destination { get; }

    /// <summary>
    /// Gets the ordered list of MIDI modifiers that process messages in this route.
    /// </summary>
    public IReadOnlyList<MidiModifier> Processors
    {
        get
        {
            lock (_lock)
            {
                return new List<MidiModifier>(_processorsStaging);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this route has encountered a non-recoverable error.
    /// Once faulted, a route will stop processing messages.
    /// </summary>
    public bool IsFaulted { get; private set; }

    /// <summary>
    /// Occurs when an unrecoverable error happens while processing a message, such as a device failure.
    /// </summary>
    internal event Action<MidiRoute, IError?>? OnError;

    // Staging list for thread-safe modifications
    private readonly List<MidiModifier> _processorsStaging = [];
    
    // Snapshot for hot-path access
    private volatile MidiModifier[] _processorsSnapshot = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MidiRoute"/> class.
    /// </summary>
    /// <param name="source">The source of MIDI messages.</param>
    /// <param name="destination">The destination for processed MIDI messages.</param>
    internal MidiRoute(IMidiSourceNode source, IMidiDestinationNode destination)
    {
        Source = source;
        Destination = destination;
    }

    /// <summary>
    /// Activates the route by subscribing to its source.
    /// </summary>
    internal void Start()
    {
        Source.OnMessageOutput += ProcessRoute;

        // Also subscribe to SysEx if the source supports it.
        if (Source is MidiInputNode inputNode)
        {
            inputNode.OnSysExOutput += ProcessSysExRoute;
        }
    }

    /// <summary>
    /// Deactivates the route by unsubscribing from its source.
    /// </summary>
    internal void Stop()
    {
        Source.OnMessageOutput -= ProcessRoute;

        if (Source is MidiInputNode inputNode)
        {
            inputNode.OnSysExOutput -= ProcessSysExRoute;
        }
    }

    /// <summary>
    /// Inserts a MIDI modifier into the processing chain at a specific index.
    /// </summary>
    /// <param name="index">The zero-based index at which the modifier should be inserted.</param>
    /// <param name="modifier">The modifier to insert.</param>
    public void InsertProcessor(int index, MidiModifier modifier)
    {
        lock (_lock)
        {
            _processorsStaging.Insert(Math.Clamp(index, 0, _processorsStaging.Count), modifier);
            _processorsSnapshot = _processorsStaging.ToArray();
        }
    }

    /// <summary>
    /// Adds a MIDI modifier to the end of the processing chain.
    /// </summary>
    /// <param name="modifier">The modifier to add.</param>
    public void AddProcessor(MidiModifier modifier)
    {
        lock (_lock)
        {
            _processorsStaging.Add(modifier);
            _processorsSnapshot = _processorsStaging.ToArray();
        }
    }

    /// <summary>
    /// Removes a MIDI modifier from the processing chain.
    /// </summary>
    /// <param name="modifier">The modifier to remove.</param>
    /// <returns>True if the modifier was found and removed; otherwise, false.</returns>
    public bool RemoveProcessor(MidiModifier modifier)
    {
        lock (_lock)
        {
            var removed = _processorsStaging.Remove(modifier);
            if (removed)
            {
                _processorsSnapshot = _processorsStaging.ToArray();
            }
            return removed;
        }
    }

    private void ProcessRoute(MidiMessage message)
    {
        if (IsFaulted) return;

        IEnumerable<MidiMessage> messagesToProcess = [message];

        // Read snapshot directly to avoid lock
        var currentProcessors = _processorsSnapshot;

        foreach (var processor in currentProcessors)
        {
            if (!processor.IsEnabled) continue;

            // Apply the processor to each message from the previous stage
            messagesToProcess = messagesToProcess.SelectMany(processor.Process);
        }


        // Send the final set of messages to the destination
        foreach (var finalMessage in messagesToProcess)
        {
            var result = Destination.ProcessMessage(finalMessage);
            if (result.IsFailure)
            {
                IsFaulted = true;
                Log.Error($"[MIDI Route Fault] Route from '{Source.Name}' to '{Destination.Name}' failed: {result.Error?.Message}");
                OnError?.Invoke(this, result.Error);
            }
        }
    }

    private void ProcessSysExRoute(byte[] data)
    {
        if (IsFaulted) return;

        // SysEx messages bypass the standard modifier chain and are routed directly to physical output devices.
        if (Destination is MidiOutputNode { Device.IsDisposed: false } outputNode)
        {
            var result = outputNode.Device.SendSysEx(data);
            if (result.IsFailure)
            {
                IsFaulted = true;
                Log.Error($"[MIDI Route Fault] SysEx route from '{Source.Name}' to '{Destination.Name}' failed: {result.Error?.Message}");
                OnError?.Invoke(this, result.Error);
            }
        }
    }
}