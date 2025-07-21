using SoundFlow.Enums;
using SoundFlow.Interfaces;
using System.Collections.Concurrent;
using SoundFlow.Abstracts.Devices;

namespace SoundFlow.Providers;

/// <summary>
/// Provides audio data from a microphone by subscribing to a specific capture device.
/// </summary>
public class MicrophoneDataProvider : ISoundDataProvider
{
    private readonly AudioDevice _captureDevice;
    private readonly ConcurrentQueue<float[]> _bufferQueue = new();
    private readonly int _bufferSize;
    private bool _isCapturing;
    private float[]? _currentBuffer;
    private int _currentBufferIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="MicrophoneDataProvider"/> class.
    /// </summary>
    /// <param name="captureDevice">The capture device to source audio from.</param>
    /// <param name="bufferSize">The size of internal audio buffers in samples.</param>
    public MicrophoneDataProvider(AudioDevice captureDevice, int bufferSize = 8)
    {
        _captureDevice = captureDevice;
        _bufferSize = bufferSize;
        SampleRate = captureDevice.Format.SampleRate;
        
        switch (captureDevice)
        {
            case AudioCaptureDevice audioCaptureDevice:
                audioCaptureDevice.OnAudioProcessed += OnAudioDataReceived;
                break;
            case FullDuplexDevice fullDuplexDevice:
                fullDuplexDevice.OnAudioProcessed += OnAudioDataReceived;
                break;
        }
    }

    /// <inheritdoc />
    public int Position { get; private set; }

    /// <inheritdoc />
    public int Length => -1; // Unknown length for a live stream

    /// <inheritdoc />
    public bool CanSeek => false;

    /// <inheritdoc />
    public SampleFormat SampleFormat => _captureDevice.Format.Format;

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <summary>
    ///     Starts capturing audio data from the microphone.
    /// </summary>
    public void StartCapture()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_isCapturing)
            return;

        _isCapturing = true;
    }

    /// <summary>
    ///     Stops capturing audio data from the microphone.
    /// </summary>
    public void StopCapture()
    {
        if (!_isCapturing) return;

        _isCapturing = false;
        
        if (_currentBuffer != null && _currentBufferIndex > 0)
        {
            // Create a new array with the actual data length
            var remainingBuffer = new float[_currentBufferIndex];
            Array.Copy(_currentBuffer, remainingBuffer, _currentBufferIndex);
            _bufferQueue.Enqueue(remainingBuffer);
            _currentBuffer = null;
            _currentBufferIndex = 0;
        }

        EndOfStreamReached?.Invoke(this, EventArgs.Empty);
    }

    private void OnAudioDataReceived(Span<float> samples, Capability capability)
    {
        if (!_isCapturing || capability != Capability.Record || IsDisposed)
            return;

        var samplesRemaining = samples.Length;
        var samplesReadPosition = 0;

        while (samplesRemaining > 0)
        {
            // Create a new buffer if the current one is null
            if (_currentBuffer == null)
            {
                _currentBuffer = new float[_bufferSize];
                _currentBufferIndex = 0;
            }

            var spaceLeftInBuffer = _bufferSize - _currentBufferIndex;
            var samplesToCopy = Math.Min(samplesRemaining, spaceLeftInBuffer);

            samples.Slice(samplesReadPosition, samplesToCopy).CopyTo(_currentBuffer.AsSpan(_currentBufferIndex));

            _currentBufferIndex += samplesToCopy;
            samplesReadPosition += samplesToCopy;
            samplesRemaining -= samplesToCopy;

            // Enqueue the buffer if it's full
            if (_currentBufferIndex == _bufferSize)
            {
                _bufferQueue.Enqueue(_currentBuffer);
                _currentBuffer = null; // Reset the current buffer
            }
        }
    }

    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed) return 0;
        var bytesCopied = 0;

        while (bytesCopied < buffer.Length && _bufferQueue.TryDequeue(out var audioData))
        {
            var remainingBufferSpace = buffer.Length - bytesCopied;
            var bytesToCopy = Math.Min(remainingBufferSpace, audioData.Length);

            audioData.AsSpan(0, bytesToCopy).CopyTo(buffer[bytesCopied..]);

            bytesCopied += bytesToCopy;
            Position += bytesToCopy;

            // If audioData has more data than the remaining buffer space, requeue the remaining portion
            if (audioData.Length > bytesToCopy)
            {
                var remainingAudioData = audioData.AsSpan(bytesToCopy).ToArray();
                _bufferQueue.Enqueue(remainingAudioData);
            }
        }

        // If we've copied less than the buffer length, it means the queue is empty
        if (bytesCopied < buffer.Length)
        {
            // Optionally fill the remainder of the buffer with silence or a default value
            buffer[bytesCopied..].Clear(); // Fill with silence
            bytesCopied = buffer.Length; // Indicate that the buffer is "full" even though it's silence
        }

        PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));
        return bytesCopied;
    }

    /// <inheritdoc />
    public void Seek(int offset) => throw new NotSupportedException("Cannot seek a live stream.");

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;
        StopCapture();
        _bufferQueue.Clear();
        switch (_captureDevice)
        {
            case AudioCaptureDevice audioCaptureDevice:
                audioCaptureDevice.OnAudioProcessed -= OnAudioDataReceived;
                break;
            case FullDuplexDevice fullDuplexDevice:
                fullDuplexDevice.OnAudioProcessed -= OnAudioDataReceived;
                break;
        }
        IsDisposed = true;
    }
}
