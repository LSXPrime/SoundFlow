using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SoundFlow.Components;

namespace SoundFlow.Abstracts;

/// <summary>
///     Base class for audio processing components.
/// </summary>
public abstract class SoundComponent
{
    private static readonly ArrayPool<float> BufferPool = ArrayPool<float>.Shared;

    // Connection state
    private readonly List<SoundComponent> _inputs = [];
    private readonly List<SoundComponent> _outputs = [];
    private readonly object _connectionsLock = new();

    // Processing state
    private readonly List<SoundModifier> _modifiers = [];
    private readonly List<AudioAnalyzer> _analyzers = [];
    private float _pan;
    private bool _solo;
    private float _volume = 1f;
    private Vector2 _volumePanFactors = Vector2.One;
    private Vector2 _previousVolumePanFactors = Vector2.One;
    private readonly object _stateLock = new();

    /// <summary>
    ///     Name of the component
    /// </summary>
    public virtual string Name { get; set; } = "Component";
    
    /// <summary>
    ///     Parent mixer of the component
    /// </summary>
    public Mixer? Parent { get; set; } = Mixer.Master;

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
    public virtual float Volume
    {
        get => _volume;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            lock (_stateLock)
            {
                _volume = value;
                UpdateVolumePanFactors();
            }
        }
    }

    /// <summary>
    ///     Pan of the component
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the pan is outside the range [0, 1].</exception>
    public virtual float Pan
    {
        get => _pan;
        set
        {
            if (value is < 0f or > 1f) throw new ArgumentOutOfRangeException(nameof(value));
            lock (_stateLock)
            {
                _pan = value;
                UpdateVolumePanFactors();
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
    public virtual bool Solo
    {
        get => _solo;
        set
        {
            lock (_stateLock)
            {
                _solo = value;
                if (_solo) AudioEngine.Instance.SoloComponent(this);
                else AudioEngine.Instance.UnsoloComponent(this);
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
    public IReadOnlyList<SoundModifier> Modifiers
    {
        get
        {
            lock (_stateLock) return new List<SoundModifier>(_modifiers);
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

    private void UpdateVolumePanFactors()
    {
        _previousVolumePanFactors = _volumePanFactors;
        var pan = Math.Clamp(_pan, 0f, 1f);
        _volumePanFactors = new Vector2(
            _volume * MathF.Sqrt(1f - pan),
            _volume * MathF.Sqrt(pan)
        );
    }

    /// <summary>
    ///     Connects this component to another component.
    /// </summary>
    /// <param name="input">The component to connect to.</param>
    /// <exception cref="InvalidOperationException">Thrown if the connection would create a cycle.</exception>
    public void ConnectInput(SoundComponent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input == this) throw new InvalidOperationException("Cannot connect to self");

        // Determine lock order to avoid deadlocks
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
    public void DisconnectInput(SoundComponent input)
    {
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

            List<SoundComponent> outputs;
            lock (current._connectionsLock)
                outputs = new List<SoundComponent>(current._outputs);

            foreach (var output in outputs)
                queue.Enqueue(output);
        }

        return false;
    }

    /// <summary>
    ///     Adds a modifier to the component.
    /// </summary>
    /// <param name="modifier">The modifier to add.</param>
    public void AddModifier(SoundModifier modifier)
    {
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
    public void RemoveModifier(SoundModifier modifier)
    {
        lock (_stateLock)
            _modifiers.Remove(modifier);
    }

    /// <summary>
    ///     Adds an analyzer to the component.
    /// </summary>
    /// <param name="analyzer">The analyzer to add.</param>
    public void AddAnalyzer(AudioAnalyzer analyzer)
    {
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
    public void RemoveAnalyzer(AudioAnalyzer analyzer)
    {
        lock (_stateLock)
            _analyzers.Remove(analyzer);
    }

    internal void Process(Span<float> outputBuffer)
    {
        if (!Enabled || Mute) return;

        float[]? rentedBuffer = null;
        try
        {
            rentedBuffer = BufferPool.Rent(outputBuffer.Length);
            var workingBuffer = rentedBuffer.AsSpan(0, outputBuffer.Length);
            workingBuffer.Clear();

            // 1. Process inputs using array pooling
            SoundComponent[] inputs;
            lock (_connectionsLock)
            {
                inputs = _inputs.Count == 0 ? [] : _inputs.ToArray();
            }

            foreach (var input in inputs)
                input.Process(workingBuffer);

            // 2. Generate audio directly
            GenerateAudio(workingBuffer);

            // 3. Process modifiers with array pooling
            SoundModifier[] modifiers;
            AudioAnalyzer[] analyzers;
            Vector2 currentVolumePan;

            lock (_stateLock)
            {
                modifiers = _modifiers.Count == 0 ? [] : _modifiers.ToArray();
                analyzers = _analyzers.Count == 0 ? [] : _analyzers.ToArray();

                currentVolumePan = Vector2.Lerp(
                    _previousVolumePanFactors,
                    _volumePanFactors,
                    Math.Clamp(128f / workingBuffer.Length, 0, 1)
                );
            }

            // 4. Process modifiers with pre-allocated array
            foreach (var modifier in modifiers)
                if (modifier.Enabled)
                    modifier.Process(workingBuffer);

            // 5. Apply volume/pan using SIMD
            ApplyVolumeAndPanning(workingBuffer, currentVolumePan);

            // 6. Mix using buffer pooling
            MixBuffers(workingBuffer, outputBuffer);

            // 7. Process analyzers with final audio
            foreach (var analyzer in analyzers)
                analyzer.Process(workingBuffer);
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

        while (count < simdLength)
        {
            var vs = new Vector<float>(source[count..]);
            var vd = new Vector<float>(destination[count..]);
            (vd + vs).CopyTo(destination[count..]);
            count += Vector<float>.Count;
        }

        // Scalar remainder
        while (count < source.Length)
        {
            destination[count] += source[count];
            count++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyVolumeAndPanning(Span<float> buffer, Vector2 volumePan)
    {
        switch (AudioEngine.Channels)
        {
            case 1:
                ApplyMonoVolume(buffer, volumePan.X + volumePan.Y);
                break;
            case 2:
                ApplyStereoVolume(buffer, volumePan);
                break;
            default:
                ApplyMultiChannelVolume(buffer, AudioEngine.Channels, volumePan);
                break;
        }
    }

    private static void ApplyMonoVolume(Span<float> buffer, float volume)
    {
        if (Math.Abs(volume - 1f) < 1e-6) return;

        if (Vector.IsHardwareAccelerated)
        {
            var vecVolume = new Vector<float>(volume);
            var count = 0;
            for (; count <= buffer.Length - Vector<float>.Count; count += Vector<float>.Count)
            {
                var vec = new Vector<float>(buffer[count..]);
                (vec * vecVolume).CopyTo(buffer[count..]);
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
        // Early exit for unity volume with more precise comparison
        if ((volume.X - 1f) * (volume.X - 1f) + (volume.Y - 1f) * (volume.Y - 1f) < 1e-12f)
            return;

        // Precompute values for scalar path and odd sample handling
        var volX = volume.X;
        var volY = volume.Y;
        var midVolume = (volX + volY) * 0.5f;

        if (Vector.IsHardwareAccelerated)
        {
            // Use MemoryMarshal.Cast for cleaner SIMD code
            var vectorBuffer = MemoryMarshal.Cast<float, Vector2>(buffer);
            foreach (ref var sample in vectorBuffer)
            {
                sample *= volume;
            }

            // Handle remaining elements using optimized pattern
            var processed = vectorBuffer.Length * 2;
            if (processed < buffer.Length)
            {
                buffer[processed] *= midVolume;
            }
        }
        else
        {
            // Optimized scalar path with loop unrolling
            var i = 0;
            for (; i < buffer.Length - 1; i += 2)
            {
                buffer[i] *= volX;
                buffer[i + 1] *= volY;
            }

            // Handle last odd element if exists
            if (i < buffer.Length)
            {
                buffer[i] *= midVolume;
            }
        }
    }

    private static void ApplyMultiChannelVolume(Span<float> buffer, int channels, Vector2 volumePan)
    {
        if (channels < 2) return;

        var weights = new float[channels];
        weights[0] = volumePan.X;
        weights[1] = volumePan.Y;
        var avg = (volumePan.X + volumePan.Y) / 2;

        for (var i = 2; i < channels; i++)
            weights[i] = avg;

        for (var i = 0; i < buffer.Length; i++)
            buffer[i] *= weights[i % channels];
    }

    /// <summary>
    ///     Generates audio data for the component.
    /// </summary>
    /// <param name="buffer">The buffer to write audio data to.</param>
    protected abstract void GenerateAudio(Span<float> buffer);
}