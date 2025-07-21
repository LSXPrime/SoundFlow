using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace SoundFlow.Providers;

/// <summary>
///     Defines the behavior for when an attempt is made to add samples to a full QueueDataProvider.
/// </summary>
public enum QueueFullBehavior
{
    /// <summary>
    ///     Throw an <see cref="InvalidOperationException"/> when the queue is full. This is the default behavior.
    /// </summary>
    Throw,

    /// <summary>
    ///     Block the calling thread until space becomes available in the queue.
    /// </summary>
    Block,

    /// <summary>
    ///     Silently drop the incoming samples and return immediately.
    /// </summary>
    Drop
}

/// <summary>
///     Provides audio data from an in-memory queue that is fed samples externally.
///     This provider is ideal for scenarios where audio data is generated or received in chunks.
/// </summary>
public class QueueDataProvider : ISoundDataProvider
{
    private readonly object _lock = new();
    private readonly Queue<float> _sampleQueue = new();
    private readonly int? _maxSamples;
    private readonly QueueFullBehavior _fullBehavior;

    private bool _isAddingCompleted;
    private bool _endOfStreamFired;
    private long _totalSamplesEnqueued;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QueueDataProvider" /> class.
    /// </summary>
    /// <param name="format">The audio format containing channels and sample rate and sample format</param>
    /// <param name="maxSamples">
    ///     The maximum number of samples the queue can hold.
    ///     If null, the queue has no size limit (and the 'full' behavior is irrelevant).
    /// </param>
    /// <param name="fullBehavior">
    ///     The behavior to exhibit when <see cref="AddSamples"/> is called on a full queue.
    ///     This parameter is ignored if <paramref name="maxSamples"/> is null.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if sampleRate is not positive.</exception>
    public QueueDataProvider(AudioFormat format, int? maxSamples = null, QueueFullBehavior fullBehavior = QueueFullBehavior.Throw)
    {
        if (format.SampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(format), "Sample rate must be positive.");
        
        if (!maxSamples.HasValue && fullBehavior != QueueFullBehavior.Throw)
            throw new ArgumentException("QueueFullBehavior cannot be set to Block or Drop for a queue with no sample limit.", nameof(fullBehavior));
        
        SampleRate = format.SampleRate;
        SampleFormat = format.Format;
        _maxSamples = maxSamples;
        _fullBehavior = fullBehavior;
    }

    #region Properties
    
    /// <inheritdoc />
    public int Position { get; private set; }

    /// <inheritdoc />
    public int Length => -1; // Length is unknown as it's a queue.

    /// <inheritdoc />
    public bool CanSeek => false;

    /// <inheritdoc />
    public SampleFormat SampleFormat { get; }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <summary>
    ///     Gets the number of samples currently available in the queue.
    /// </summary>
    public int SamplesAvailable
    {
        get
        {
            lock (_lock)
            {
                return _sampleQueue.Count;
            }
        }
    }
    
    /// <summary>
    ///     Gets the total number of samples enqueued so far.
    /// </summary>
    public long TotalSamplesEnqueued
    {
        get 
        { 
            lock (_lock)
            {
                return _totalSamplesEnqueued;
            } 
        }
    }

    #endregion

    #region Events

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    #endregion

    /// <summary>
    ///     Adds audio samples to the queue. The behavior when the queue is full is determined
    ///     by the <see cref="QueueFullBehavior"/> set in the constructor.
    /// </summary>
    /// <param name="samples">The buffer containing the samples to add.</param>
    public void AddSamples(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return;
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        lock (_lock)
        {
            if (_isAddingCompleted)
                throw new InvalidOperationException("Cannot add samples after CompleteAdding has been called.");

            if (_maxSamples.HasValue && _sampleQueue.Count + samples.Length > _maxSamples.Value)
            {
                switch (_fullBehavior)
                {
                    case QueueFullBehavior.Throw:
                        throw new InvalidOperationException("Adding these samples would exceed the maximum size of the queue.");

                    case QueueFullBehavior.Drop:
                        return; // Silently drop the samples and return.

                    case QueueFullBehavior.Block:
                        // Block until space is available for the entire sample block.
                        while (!IsDisposed && _sampleQueue.Count + samples.Length > _maxSamples.Value)
                        {
                            Monitor.Wait(_lock);
                        }
                        // Re-check disposed status after waking up.
                        ObjectDisposedException.ThrowIf(IsDisposed, this);
                        break;
                }
            }
            
            foreach (var sample in samples)
            {
                _sampleQueue.Enqueue(sample);
            }
            _totalSamplesEnqueued += samples.Length;
        }
    }

    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed || buffer.IsEmpty) return 0;
        
        var samplesRead = 0;
        var shouldFireEndOfStream = false;
        var spaceWasFreed = false;

        lock (_lock)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                if (_sampleQueue.TryDequeue(out var sample))
                {
                    buffer[i] = sample;
                    samplesRead++;
                }
                else
                {
                    break; // Queue is empty.
                }
            }

            if (samplesRead > 0)
            {
                Position += samplesRead;
                spaceWasFreed = true;
            }

            if (_sampleQueue.Count == 0 && _isAddingCompleted && !_endOfStreamFired)
            {
                shouldFireEndOfStream = true;
                _endOfStreamFired = true;
            }
            
            // If space was freed, notify any waiting producer threads.
            if (spaceWasFreed)
            {
                Monitor.PulseAll(_lock);
            }
        }

        if (samplesRead > 0) PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));

        if (shouldFireEndOfStream) EndOfStreamReached?.Invoke(this, EventArgs.Empty);

        return samplesRead;
    }

    /// <summary>
    ///     Resets the provider to its initial state, clearing the sample queue and resetting the position.
    ///     This allows the instance to be reused. Any threads blocked in <see cref="AddSamples"/> will be unblocked.
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        lock (_lock)
        {
            _sampleQueue.Clear();
            Position = 0;
            _totalSamplesEnqueued = 0;
            _isAddingCompleted = false;
            _endOfStreamFired = false;
            
            // Wake up any threads that were blocked, as the queue is now empty.
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>
    ///     Marks the end of sample addition, indicating that the producer is finished adding samples.
    ///     Any threads blocked in <see cref="ReadBytes"/> will be unblocked.
    /// </summary>
    public void CompleteAdding()
    {
        lock (_lock)
        {
            _isAddingCompleted = true;
            Monitor.PulseAll(_lock);
        }
    }

    /// <inheritdoc />
    public void Seek(int offset) => throw new InvalidOperationException("Seeking is not supported by the QueueDataProvider.");

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;
        
        lock (_lock)
        {
            if (IsDisposed) return;
            
            IsDisposed = true;
            _sampleQueue.Clear();

            Monitor.PulseAll(_lock);
        }

        EndOfStreamReached = null;
        PositionChanged = null;
    }
}