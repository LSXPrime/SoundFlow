using System.Numerics;
using SoundFlow.Abstracts;
using SoundFlow.Utils;

namespace SoundFlow.Experimental;

/// <summary> 
/// A noise suppression effect that attempts to isolate human speech by attenuating  
/// frequencies outside a typical vocal range. This acts as a band-pass filter. 
/// </summary>
/// <remarks> 
/// It Introduces a noise dithering effect to the audio, which can be eliminated by using WebRTC Noise Suppression.
/// </remarks>
public class VoiceIsolationEffect : SoundModifier
{
    /// <inheritdoc/>
    public override string Name { get; set; } = "Voice Isolation Effect";

    /// <summary> 
    /// The audio sample rate (e.g., 44100 Hz, 48000 Hz). 
    /// This is crucial for correctly mapping FFT bins to frequencies. 
    /// </summary> 
    private readonly int _sampleRate;

    /// <summary> 
    /// The lower bound of the frequency range to preserve (in Hz). 
    /// Frequencies below this will be silenced. 
    /// </summary> 
    public float MinFrequency { get; set; }

    /// <summary> 
    /// The upper bound of the frequency range to preserve (in Hz). 
    /// Frequencies above this will be silenced. 
    /// </summary> 
    public float MaxFrequency { get; set; }

    /// <summary> 
    /// The size of the Fast Fourier Transform (FFT) window. 
    /// This determines the frequency resolution of the effect. 
    /// </summary>
    public int FftSize { get; private set; } // Typical sizes: 1024, 2048, 4096

    /// <summary> 
    /// The hop size of the FFT. 
    /// This determines the overlap between consecutive FFT windows. 
    /// </summary>
    public int HopSize { get; private set; } // Typically FftSize/4 or FftSize/2

    private readonly float[] _window;
    private readonly float[] _overlapBuffer;

    /// <summary>
    /// Constructs a new instance of <see cref="VoiceIsolationEffect"/>.
    /// </summary>
    /// <param name="sampleRate">The audio sample rate in Hz, e.g., 44100 Hz or 48000 Hz.</param>
    /// <param name="minFrequency">The lower bound of the frequency range to preserve, in Hz. Defaults to 300 Hz.</param>
    /// <param name="maxFrequency">The upper bound of the frequency range to preserve, in Hz. Defaults to 3400 Hz.</param>
    /// <param name="fftSize">The size of the FFT window. Determines frequency resolution. Defaults to 2048.</param>
    /// <param name="hopSize">The hop size of the FFT. Determines overlap between consecutive windows. Defaults to 512.</param>
    public VoiceIsolationEffect(int sampleRate, float minFrequency = 300f, float maxFrequency = 3400f,
        int fftSize = 2048, int hopSize = 512)
    {
        _sampleRate = sampleRate;
        MinFrequency = minFrequency;
        MaxFrequency = maxFrequency;
        FftSize = fftSize;
        HopSize = hopSize;

        // Initialize window function (Hann window)
        _window = new float[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1))));
        }

        _overlapBuffer = new float[FftSize];
    }

    public override void Process(Span<float> buffer, int channels)
    {
        if (!Enabled || channels <= 0) return;

        for (var c = 0; c < channels; c++)
        {
            ProcessChannel(buffer, c, channels);
        }
    }

    public override float ProcessSample(float sample, int channel) => throw new NotImplementedException();

    private void ProcessChannel(Span<float> buffer, int channel, int totalChannels)
    {
        var samplesProcessed = 0;
        var frameCount = buffer.Length / totalChannels;

        while (samplesProcessed + FftSize <= frameCount)
        {
            // 1. Apply window function and convert to Complex
            var complexBuffer = new Complex[FftSize];
            for (var i = 0; i < FftSize; i++)
            {
                var index = (samplesProcessed + i) * totalChannels + channel;
                complexBuffer[i] = new Complex(buffer[index] * _window[i], 0);
            }

            // 2. Forward FFT
            MathHelper.Fft(complexBuffer);

            // 3. Apply smoother frequency mask
            for (var i = 0; i < FftSize / 2; i++)
            {
                var frequency = (double)i * _sampleRate / FftSize;
                var gain = GetFrequencyGain(frequency);

                complexBuffer[i] *= gain;
                if (i > 0) complexBuffer[FftSize - i] *= gain;
            }

            // 4. Inverse FFT
            MathHelper.InverseFft(complexBuffer);

            // 5. Apply window again and overlap-add
            for (var i = 0; i < FftSize; i++)
            {
                var outputIndex = (samplesProcessed + i) * totalChannels + channel;
                var outputSample = (float)complexBuffer[i].Real * _window[i];

                // Overlap-add
                if (i < HopSize && samplesProcessed > 0)
                    buffer[outputIndex] = _overlapBuffer[i] + outputSample;
                else
                    buffer[outputIndex] = outputSample;

                // Store for next overlap
                if (i >= HopSize) _overlapBuffer[i - HopSize] = outputSample;
            }

            samplesProcessed += HopSize;
        }
    }

    private double GetFrequencyGain(double frequency)
    {
        // Smooth transition band
        const double transitionWidth = 100.0; // Hz for transition band

        if (frequency < MinFrequency - transitionWidth || frequency > MaxFrequency + transitionWidth)
            return 0.0;

        if (frequency >= MinFrequency && frequency <= MaxFrequency)
            return 1.0;

        // Transition regions
        var t = frequency < MinFrequency
            ? (frequency - (MinFrequency - transitionWidth)) / transitionWidth
            : ((MaxFrequency + transitionWidth) - frequency) / transitionWidth;
        return 0.5 * (1 - Math.Cos(Math.PI * t)); // Raised cosine transition
    }
}