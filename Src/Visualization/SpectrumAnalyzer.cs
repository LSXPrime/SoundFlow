using SoundFlow.Abstracts;
using SoundFlow.Utils;
using System.Numerics;
using SoundFlow.Interfaces;
using SoundFlow.Structs;

namespace SoundFlow.Visualization
{
    /// <summary>
    /// Analyzes audio data to provide frequency spectrum information using FFT.
    /// </summary>
    public class SpectrumAnalyzer : AudioAnalyzer
    {
        private readonly int _fftSize;
        private readonly float[] _spectrumData;
        private readonly Complex[] _fftBuffer;
        private readonly float[] _window;

        /// <inheritdoc />
        public override string Name { get; set; } = "Spectrum Analyzer";

        /// <summary>
        /// Initializes a new instance of the <see cref="SpectrumAnalyzer"/> class.
        /// </summary>
        /// <param name="format">The audio format to analyze.</param>
        /// <param name="fftSize">The size of the FFT. Must be a power of 2.</param>
        /// <param name="visualizer">The visualizer to send data to.</param>
        /// <exception cref="ArgumentException"></exception>
        public SpectrumAnalyzer(AudioFormat format, int fftSize, IVisualizer? visualizer = null) : base(format, visualizer)
        {
            if ((fftSize & (fftSize - 1)) != 0) // Check if fftSize is a power of 2
            {
                throw new ArgumentException("FFT size must be a power of 2.");
            }

            _fftSize = fftSize;
            _spectrumData = new float[_fftSize / 2];
            _fftBuffer = new Complex[_fftSize];
            _window = MathHelper.HammingWindow(_fftSize);
        }

        /// <summary>
        /// Gets the spectrum data.
        /// </summary>
        public float[] SpectrumData => _spectrumData;

        /// <inheritdoc/>
        protected override void Analyze(ReadOnlySpan<float> buffer, int channels)
        {
            // Apply window function and copy to FFT buffer
            var numSamples = Math.Min(buffer.Length, _fftSize);
            for (var i = 0; i < numSamples; i++)
            {
                _fftBuffer[i] = new Complex(buffer[i] * _window[i], 0);
            }

            for (var i = numSamples; i < _fftSize; i++)
            {
                _fftBuffer[i] = Complex.Zero;
            }

            // Perform FFT
            MathHelper.Fft(_fftBuffer);

            // Calculate magnitude spectrum
            for (var i = 0; i < _fftSize / 2; i++)
            {
                _spectrumData[i] = (float)_fftBuffer[i].Magnitude;
            }
        }
    }
}
