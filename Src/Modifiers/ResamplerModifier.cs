using SoundFlow.Abstracts;
using SoundFlow.Interfaces;

namespace SoundFlow.Modifiers;

/// <summary>
/// A real-time resampling modifier that changes the playback speed and pitch of an audio signal.
/// This is achieved by changing the number of samples in the audio stream, which affects both
/// its duration and perceived pitch.
/// </summary>
/// <remarks>
/// Due to the nature of resampling, this modifier introduces a small amount of latency as it needs
/// to buffer incoming audio to perform its calculations.
/// </remarks>
public class ResamplerModifier : SoundModifier
{
    private float _resampleFactor = 1.0f;
    private readonly List<float> _inputBuffer = [];
    private double _readPosition; // The fractional frame position in the input buffer.

    /// <inheritdoc />
    public override string Name { get; set; } = "Resampler";

    /// <summary>
    /// Gets or sets the resampling factor. 
    /// Values greater than 1.0 speed up playback and increase pitch.
    /// Values less than 1.0 slow down playback and decrease pitch.
    /// A value of 1.0 results in no change.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the value is less than or equal to zero.</exception>
    [ControllableParameter("Factor", 0.1, 4.0)]
    public float ResampleFactor
    {
        get => _resampleFactor;
        set
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "ResampleFactor must be greater than zero.");
            
            _resampleFactor = value;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResamplerModifier"/> class with a specific factor.
    /// </summary>
    /// <param name="resampleFactor">The initial resampling factor. Defaults to 1.0 (no change).</param>
    public ResamplerModifier(float resampleFactor = 1.0f)
    {
        ResampleFactor = resampleFactor;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResamplerModifier"/> class by specifying source and target sample rates.
    /// The resampling factor is calculated as `targetRate / sourceRate`.
    /// </summary>
    /// <param name="sourceRate">The original sample rate of the audio.</param>
    /// <param name="targetRate">The target sample rate to resample to, which determines the new speed and pitch.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if either sourceRate or targetRate is not a positive number.</exception>
    public ResamplerModifier(int sourceRate, int targetRate)
    {
        if (sourceRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceRate), "Source rate must be a positive number.");
        if (targetRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetRate), "Target rate must be a positive number.");
        
        ResampleFactor = (float)targetRate / sourceRate;
    }

    /// <summary>
    /// Processes a block of audio samples, resampling them according to the <see cref="ResampleFactor"/>.
    /// The modification is performed in-place on the provided buffer.
    /// </summary>
    /// <param name="buffer">The buffer containing the audio samples to modify. The resampled audio will be written back into this buffer.</param>
    /// <param name="channels">The number of channels in the buffer.</param>
    public override void Process(Span<float> buffer, int channels)
    {
        if (!Enabled || Math.Abs(_resampleFactor - 1.0f) < 1e-6f)
            return;

        // Add incoming samples to the internal buffer.
        _inputBuffer.AddRange(buffer);

        var outputFrames = buffer.Length / channels;
        var outputSamplesGenerated = 0;

        // Generate resampled output frames.
        for (var i = 0; i < outputFrames; i++)
        {
            var frameIndex0 = (int)Math.Floor(_readPosition);
            var frameIndex1 = frameIndex0 + 1;
            var fraction = _readPosition - frameIndex0;

            // Ensure we have enough samples in the buffer for interpolation.
            if (frameIndex1 * channels + (channels - 1) >= _inputBuffer.Count)
                break;

            for (var c = 0; c < channels; c++)
            {
                var sampleIndex0 = frameIndex0 * channels + c;
                var sampleIndex1 = frameIndex1 * channels + c;

                var sample0 = _inputBuffer[sampleIndex0];
                var sample1 = _inputBuffer[sampleIndex1];
                    
                // Linear interpolation.
                buffer[outputSamplesGenerated++] = (float)(sample0 + fraction * (sample1 - sample0));
            }

            _readPosition += _resampleFactor;
        }
            
        // If we couldn't fill the entire output buffer, fill the rest with silence.
        if (outputSamplesGenerated < buffer.Length)
        {
            buffer[outputSamplesGenerated..].Clear();
        }

        // Clean up the input buffer by removing consumed samples.
        var framesConsumed = (int)Math.Floor(_readPosition);
        var samplesConsumed = framesConsumed * channels;

        if (samplesConsumed > 0 && samplesConsumed <= _inputBuffer.Count)
        {
            _inputBuffer.RemoveRange(0, samplesConsumed);
            _readPosition -= framesConsumed;
        }
    }

    /// <summary>
    /// This method is not supported for the Resampler modifier.
    /// Processing must be done on a block of samples to perform interpolation.
    /// </summary>
    /// <param name="sample">The input audio sample.</param>
    /// <param name="channel">The channel the sample belongs to.</param>
    /// <returns>This method always throws an exception.</returns>
    /// <exception cref="NotSupportedException">Always thrown, as this modifier requires block-based processing.</exception>
    public override float ProcessSample(float sample, int channel)
    {
        throw new NotSupportedException("The Resampler modifier requires block-based processing and does not support per-sample processing.");
    }
}