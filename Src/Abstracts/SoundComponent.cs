using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using SoundFlow.Components;
using SoundFlow.Structs;

namespace SoundFlow.Abstracts;

/// <summary>
///     Base class for audio processing components.
/// </summary>
public abstract class SoundComponent : IDisposable
{
    private static readonly ArrayPool<float> BufferPool = ArrayPool<float>.Shared;

    // Connection state
    private readonly List<SoundComponent> _inputs = [];
    private readonly List<SoundComponent> _outputs = [];
    private readonly object _connectionsLock = new();

    // Processing state
    private readonly List<SoundModifier> _modifiers = [];
    private readonly List<AudioAnalyzer> _analyzers = [];
    private float _pan = 0.5f;
    private bool _solo;
    private float _volume = 1f;
    private Vector2 _volumePanFactors;
    private readonly object _stateLock = new();

    /// <summary>
    /// 
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets the engine context this component belongs to.
    /// </summary>
    public AudioEngine Engine { get; }
    
    /// <summary>
    /// Gets the audio format of the component.
    /// </summary>
    public AudioFormat Format { get; }

    /// <summary>
    ///     Name of the component
    /// </summary>
    public virtual string Name { get; set; } = "Component";

    /// <summary>
    ///     Parent mixer of the component
    /// </summary>
    public Mixer? Parent { get; set; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="SoundComponent" /> class.
    /// </summary>
    protected SoundComponent(AudioEngine engine, AudioFormat format)
    {
        Engine = engine;
        Format = format;
        UpdateVolumePanFactors(Format.Channels);
    }

    /// <summary>
    ///     Finalizes an instance of the <see cref="SoundComponent"/> class.
    /// </summary>
    ~SoundComponent()
    {
        Dispose(false);
    }

    /// <summary>
    ///     Input connections
    /// </summary>
    public IReadOnlyList<SoundComponent> Inputs
    {
        get
        {
            lock (_connectionsLock) return new List<SoundComponent>(_inputs);
        }
    }

    /// <summary>
    ///     Output connections
    /// </summary>
    public IReadOnlyList<SoundComponent> Outputs
    {
        get
        {
            lock (_connectionsLock) return new List<SoundComponent>(_outputs);
        }
    }

    /// <summary>
    ///     Volume of the component
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the volume is negative.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the component has been disposed.</exception>
    public virtual float Volume
    {
        get => _volume;
        set
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            lock (_stateLock)
            {
                _volume = value;
                UpdateVolumePanFactors(Format.Channels);
            }
        }
    }

    /// <summary>
    ///     Pan of the component
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the pan is outside the range [0, 1].</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the component has been disposed.</exception>
    public virtual float Pan
    {
        get => _pan;
        set
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (value is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(value), "Pan must be between 0.0 and 1.0.");
            lock (_stateLock)
            {
                _pan = value;
                UpdateVolumePanFactors(Format.Channels);
            }
        }
    }

    /// <summary>
    ///     Whether the component is enabled
    /// </summary>
    public virtual bool Enabled { get; set; } = true;

    /// <summary>
    ///     Whether the component is soloed
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the component has been disposed.</exception>
    public virtual bool Solo
    {
        get => _solo;
        set
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            lock (_stateLock)
            {
                if (_solo == value) return;
                _solo = value;
                if (_solo) Engine.SoloComponent(this);
                else Engine.UnsoloComponent(this);
            }
        }
    }

    /// <summary>
    ///     Whether the component is muted
    /// </summary>
    public virtual bool Mute { get; set; }

    /// <summary>
    ///     Modifiers applied to the component
    /// </summary>
    public IReadOnlyList<SoundModifier?> Modifiers
    {
        get
        {
            lock (_stateLock) return new List<SoundModifier?>(_modifiers);
        }
    }

    /// <summary>
    ///     Analyzers applied to the component
    /// </summary>
    public IReadOnlyList<AudioAnalyzer> Analyzers
    {
        get
        {
            lock (_stateLock) return new List<AudioAnalyzer>(_analyzers);
        }
    }

    private void UpdateVolumePanFactors(int channels)
    {
        var panValue = Math.Clamp(_pan, 0f, 1f);
        if (channels == 1)
        {
             // For mono, combine pan to a single gain factor.
            _volumePanFactors = new Vector2(_volume, 0);
        }
        else
        {
            _volumePanFactors = new Vector2(
                _volume * MathF.Sqrt(1f - panValue),
                _volume * MathF.Sqrt(panValue)
            );
        }
    }

    /// <summary>
    ///     Connects this component to another component.
    /// </summary>
    /// <param name="input">The component to connect to.</param>
    /// <exception cref="InvalidOperationException">Thrown if the connection would create a cycle.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the component has been disposed.</exception>
    public void ConnectInput(SoundComponent input)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(input);
        if (input.IsDisposed) throw new ArgumentException("Cannot connect to a disposed component.", nameof(input));
        if (input == this) throw new InvalidOperationException("Cannot connect to self");

        SoundComponent first, second;
        if (GetHashCode() < input.GetHashCode())
        {
            first = this;
            second = input;
        }
        else
        {
            first = input;
            second = this;
        }

        lock (first._connectionsLock)
        lock (second._connectionsLock)
        {
            if (_inputs.Contains(input)) return;

            if (IsReachable(input, this))
                throw new InvalidOperationException("Connection would create a cycle");

            _inputs.Add(input);
            input._outputs.Add(this);
        }
    }

    /// <summary>
    ///     Disconnects this component from another component.
    /// </summary>
    /// <param name="input">The component to disconnect from.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the component has been disposed.</exception>
    public void DisconnectInput(SoundComponent input)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(input);

        // If the other component is disposed, its connections are already being torn down.
        if (input.IsDisposed) return;

        lock (_connectionsLock)
        {
            if (!_inputs.Remove(input)) return;

            lock (input._connectionsLock)
                input._outputs.Remove(this);
        }
    }

    private static bool IsReachable(SoundComponent start, SoundComponent target)
    {
        var visited = new HashSet<SoundComponent>();
        var queue = new Queue<SoundComponent>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target) return true;
            if (!visited.Add(current)) continue;

            List<SoundComponent> currentOutputs;
            lock (current._connectionsLock)
                currentOutputs = [..current._outputs];

            foreach (var output in currentOutputs)
                queue.Enqueue(output);
        }

        return false;
    }

    /// <summary>
    ///     Adds a modifier to the component.
    /// </summary>
    /// <param name="modifier">The modifier to add.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the component has been disposed.</exception>
    public void AddModifier(SoundModifier modifier)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        lock (_stateLock)
        {
            if (!_modifiers.Contains(modifier))
                _modifiers.Add(modifier);
        }
    }

    /// <summary>
    ///     Removes a modifier from the component.
    /// </summary>
    /// <param name="modifier">The modifier to remove.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the component has been disposed.</exception>
    public void RemoveModifier(SoundModifier modifier)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        lock (_stateLock)
            _modifiers.Remove(modifier);
    }

    /// <summary>
    ///     Adds an analyzer to the component.
    /// </summary>
    /// <param name="analyzer">The analyzer to add.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the component has been disposed.</exception>
    public void AddAnalyzer(AudioAnalyzer analyzer)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        lock (_stateLock)
        {
            if (!_analyzers.Contains(analyzer))
                _analyzers.Add(analyzer);
        }
    }

    /// <summary>
    ///     Removes an analyzer from the component.
    /// </summary>
    /// <param name="analyzer">The analyzer to remove.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the component has been disposed.</exception>
    public void RemoveAnalyzer(AudioAnalyzer analyzer)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        lock (_stateLock)
            _analyzers.Remove(analyzer);
    }

    internal void Process(Span<float> outputBuffer, int channels)
    {
        if (!Enabled || Mute || IsDisposed) return;

        float[]? rentedBuffer = null;
        try
        {
            rentedBuffer = BufferPool.Rent(outputBuffer.Length);
            var workingBuffer = rentedBuffer.AsSpan(0, outputBuffer.Length);
            workingBuffer.Clear();

            SoundComponent[] currentInputs;
            lock (_connectionsLock)
            {
                currentInputs = _inputs.Count == 0 ? [] : _inputs.ToArray();
            }

            foreach (var input in currentInputs)
                input.Process(workingBuffer, channels);

            GenerateAudio(workingBuffer, channels);

            SoundModifier[] currentModifiers;
            AudioAnalyzer[] currentAnalyzers;
            Vector2 currentVolumePan;

            lock (_stateLock)
            {
                currentModifiers = _modifiers.Count == 0 ? [] : _modifiers.ToArray();
                currentAnalyzers = _analyzers.Count == 0 ? [] : _analyzers.ToArray();
                UpdateVolumePanFactors(channels);
                currentVolumePan = _volumePanFactors;
            }

            foreach (var modifier in currentModifiers)
                if (modifier.Enabled)
                    modifier.Process(workingBuffer, channels);

            ApplyVolumeAndPanning(workingBuffer, currentVolumePan, channels);

            MixBuffers(workingBuffer, outputBuffer);

            foreach (var analyzer in currentAnalyzers)
                analyzer.Process(workingBuffer, channels);
        }
        finally
        {
            if (rentedBuffer != null)
                BufferPool.Return(rentedBuffer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MixBuffers(ReadOnlySpan<float> source, Span<float> destination)
    {
        if (source.Length != destination.Length)
            throw new ArgumentException("Source and destination buffers must have the same length.");

        // SIMD-accelerated mixing
        var count = 0;
        var simdLength = source.Length - (source.Length % Vector<float>.Count);

        // Ensure there's enough data for SIMD operations
        if (simdLength > 0 && Vector<float>.Count <= source.Length && Vector<float>.Count <= destination.Length)
        {
            while (count < simdLength)
            {
                var vs = new Vector<float>(source.Slice(count, Vector<float>.Count));
                var vd = new Vector<float>(destination.Slice(count, Vector<float>.Count));
                (vd + vs).CopyTo(destination.Slice(count, Vector<float>.Count));
                count += Vector<float>.Count;
            }
        }

        // Scalar remainder
        while (count < source.Length)
        {
            destination[count] += source[count];
            count++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ApplyVolumeAndPanning(Span<float> buffer, Vector2 volumePan, int channels)
    {
        switch (channels)
        {
            case 1:
                // Constant power calculation for mono
                var monoGain = MathF.Sqrt(volumePan.X * volumePan.X + volumePan.Y * volumePan.Y);
                ApplyMonoVolume(buffer, monoGain);
                break;
            case 2:
                ApplyStereoVolume(buffer, volumePan);
                break;
            default:
                ApplyMultiChannelVolume(buffer, channels, volumePan);
                break;
        }
    }

    private static void ApplyMonoVolume(Span<float> buffer, float volume)
    {
        if (Math.Abs(volume - 1f) < 1e-6f) return;

        if (Vector.IsHardwareAccelerated && buffer.Length >= Vector<float>.Count)
        {
            var vecVolume = new Vector<float>(volume);
            var count = 0;
            for (; count <= buffer.Length - Vector<float>.Count; count += Vector<float>.Count)
            {
                var vec = new Vector<float>(buffer.Slice(count, Vector<float>.Count));
                (vec * vecVolume).CopyTo(buffer.Slice(count, Vector<float>.Count));
            }

            for (; count < buffer.Length; count++)
                buffer[count] *= volume;
        }
        else
        {
            for (var i = 0; i < buffer.Length; i++)
                buffer[i] *= volume;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyStereoVolume(Span<float> buffer, Vector2 volume)
    {
        // Early exit for unity volume (both channels at 1.0)
        if (Math.Abs(volume.X - 1f) < 1e-7f && Math.Abs(volume.Y - 1f) < 1e-7f)
            return;

        var volX = volume.X;
        var volY = volume.Y;
        
        var i = 0;

        if (Vector.IsHardwareAccelerated && buffer.Length >= Vector<float>.Count)
        {
            var vectorSize = Vector<float>.Count;
            
            if (vectorSize % 2 == 0)
            {
                Span<float> gainFactorsSpan = stackalloc float[vectorSize];
                for (var k = 0; k < vectorSize; k += 2)
                {
                    gainFactorsSpan[k] = volX;
                    gainFactorsSpan[k + 1] = volY;
                }
                var simdGainFactors = new Vector<float>(gainFactorsSpan);

                for (; i <= buffer.Length - vectorSize; i += vectorSize)
                {
                    var audioSimd = new Vector<float>(buffer.Slice(i, vectorSize));
                    (audioSimd * simdGainFactors).CopyTo(buffer.Slice(i, vectorSize));
                }
            }
        }

        // Scalar processing for the remainder or if SIMD is not applicable/enabled
        for (; i <= buffer.Length - 2; i += 2)
        {
            buffer[i] *= volX;
            buffer[i + 1] *= volY;
        }

        // Handle the last odd element if the buffer length is odd
        if (i < buffer.Length)
        {
            buffer[i] *= (volX + volY) * 0.5f;
        }
    }
    
    private static void ApplyMultiChannelVolume(Span<float> buffer, int channels, Vector2 volumePan)
    {
        if (channels < 2) return;

        var weights = new float[channels];
        weights[0] = volumePan.X;
        weights[1] = volumePan.Y;
        var avg = (volumePan.X + volumePan.Y) * 0.5f;

        for (var i = 2; i < channels; i++)
            weights[i] = avg;

        for (var i = 0; i < buffer.Length; i++)
            buffer[i] *= weights[i % channels];
    }

    /// <summary>
    ///     Generates audio data for the component.
    /// </summary>
    /// <param name="buffer">The buffer to write audio data to.</param>
    /// <param name="channels">The number of channels to generate for.</param>
    protected abstract void GenerateAudio(Span<float> buffer, int channels);

    /// <inheritdoc />
    public virtual void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (IsDisposed) return;

        if (disposing)
        {
            // Stop processing immediately.
            Enabled = false;

            // Unregister from the engine's solo system.
            if (_solo) Engine.UnsoloComponent(this);

            // Explicitly remove from parent mixer.
            Parent?.RemoveComponent(this);
            Parent = null;

            // Disconnect all inputs and outputs.
            var inputsToDisconnect = Inputs;
            var outputsToDisconnect = Outputs;

            // Tell our outputs to disconnect from us.
            foreach (var output in outputsToDisconnect)
            {
                output.DisconnectInput(this);
            }
            
            // Disconnect from our inputs.
            foreach (var input in inputsToDisconnect)
            {
                DisconnectInput(input);
            }
            
            lock (_stateLock)
            {
                _modifiers.Clear();
                _analyzers.Clear();
            }
        }
        
        // Clear connection lists. This is defensive, as they should be empty by now.
        lock (_connectionsLock)
        {
            _inputs.Clear();
            _outputs.Clear();
        }

        IsDisposed = true;
    }
}
