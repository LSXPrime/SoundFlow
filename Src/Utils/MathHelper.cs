using System.Numerics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SoundFlow.Utils;

/// <summary>
///     Helper methods for common math operations.
/// </summary>
public static class MathHelper
{
    /// <summary>
    /// Gets or sets a value indicating whether to use AVX (Advanced Vector Extensions) instructions
    /// if the hardware supports them. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Setting this to <c>false</c> will prevent the use of AVX, and the implementation will
    /// fall back to SSE or scalar code, even on AVX-capable hardware.
    /// </remarks>
    public static bool EnableAvx { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to use SSE (Streaming SIMD Extensions) instructions
    /// if the hardware supports them. Defaults to <c>true</c>.
    /// </summary>
    /// <remarks>
    /// Setting this to <c>false</c> will prevent the use of SSE, and the implementation will
    /// fall back to scalar code, even on SSE-capable hardware. This also affects AVX routines
    /// that may use SSE for specific operations.
    /// </remarks>
    public static bool EnableSse { get; set; } = true;

    /// <summary>
    /// Computes the Inverse Fast Fourier Transform (IFFT) of a complex array.
    /// </summary>
    /// <param name="data">The complex data array.</param>
    public static void InverseFft(Complex[] data)
    {
        // Conjugate the complex data
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = Complex.Conjugate(data[i]);
        }

        // Perform forward FFT
        Fft(data);

        // Conjugate and scale the result by 1/N
        var scale = 1.0 / data.Length;
        for (var i = 0; i < data.Length; i++)
        {
            // Combine final conjugation and scaling
            data[i] = new Complex(data[i].Real * scale, -data[i].Imaginary * scale);
        }
    }

    /// <summary>
    /// Computes the Fast Fourier Transform (FFT) of a complex array using SIMD acceleration with fallback to a scalar implementation.
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length.</param>
    public static void Fft(Complex[] data)
    {
        var n = data.Length;
        if (!IsPowerOfTwo(n))
        {
            throw new ArgumentException("Data length must be a power of two.", nameof(data));
        }

        if (n <= 1) return;

        // Use iterative Cooley-Tukey for SIMD, which is generally faster
        if (EnableAvx && Avx.IsSupported && n >= 4) // AVX can process 2 complex numbers (4 doubles) at a time
            FftAvx(data);
        else if (EnableSse && Sse3.IsSupported && n >= 2) // SSE3 is needed for the efficient complex multiply
            FftSse(data);
        else // Fallback to recursive implementation if no SIMD is available
            FftScalar(data);
    }

    /// <summary>
    /// Scalar recursive implementation of the Fast Fourier Transform (FFT).
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length.</param>
    private static void FftScalar(Complex[] data)
    {
        var n = data.Length;
        if (n <= 1) return;

        // Separate even and odd elements
        var even = new Complex[n / 2];
        var odd = new Complex[n / 2];
        for (var i = 0; i < n / 2; i++)
        {
            even[i] = data[2 * i];
            odd[i] = data[2 * i + 1];
        }

        // Recursive FFT on even and odd parts
        FftScalar(even);
        FftScalar(odd);

        // Combine
        for (var k = 0; k < n / 2; k++)
        {
            var t = Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI * k / n) * odd[k];
            data[k] = even[k] + t;
            data[k + n / 2] = even[k] - t;
        }
    }

    /// <summary>
    /// SSE-accelerated iterative implementation of the Fast Fourier Transform (FFT).
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length.</param>
    private static unsafe void FftSse(Complex[] data)
    {
        var n = data.Length;
        BitReverse(data);

        fixed (Complex* pData = data)
        {
            // Process stages (m = 2, 4, 8, ...)
            for (var s = 1; s <= Math.Log2(n); s++)
            {
                var m = 1 << s;
                FftSseStage(pData, n, m);
            }
        }
    }

        /// <summary>
    /// AVX-accelerated iterative implementation of the Fast Fourier Transform (FFT).
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length.</param>
    private static unsafe void FftAvx(Complex[] data)
    {
        var n = data.Length;
        BitReverse(data);

        fixed (Complex* pData = data)
        {
            // Process stages (m = 2, 4, 8, ...)
            for (var s = 1; s <= Math.Log2(n); s++)
            {
                var m = 1 << s;

                // For small butterfly sizes (m=2), AVX has overhead.
                // Use an optimized SSE stage if available, otherwise fall back to scalar.
                if (m < 4)
                {
                    if (EnableSse && Sse3.IsSupported)
                    {
                        FftSseStage(pData, n, m);
                    }
                    else
                    {
                        // Fallback to scalar per-butterfly
                        for (var k = 0; k < n; k += m)
                        {
                            for (var j = 0; j < m / 2; j++)
                            {
                                var t = Complex.FromPolarCoordinates(1.0, -2.0 * Math.PI * j / m) * pData[k + j + m / 2];
                                var temp = pData[k + j];
                                pData[k + j] = temp + t;
                                pData[k + j + m / 2] = temp - t;
                            }
                        }
                    }

                    continue; // Proceed to the next stage
                }

                // AVX stage for m >= 4
                var m2 = m >> 1;
                var wMAngle1 = -2.0 * Math.PI / m;
                var wMAngle2 = wMAngle1 * 2;

                var wM1 = Complex.FromPolarCoordinates(1.0, wMAngle1);
                var wM2Step = Complex.FromPolarCoordinates(1.0, wMAngle2);

                var vWmStep = Vector256.Create(wM2Step.Real, wM2Step.Imaginary, wM2Step.Real,
                    wM2Step.Imaginary);

                for (var k = 0; k < n; k += m)
                {
                    var vW = Vector256.Create(1.0, 0.0, wM1.Real, wM1.Imaginary);
                    for (var j = 0; j < m2; j += 2)
                    {
                        var pEven = (double*)(pData + k + j);
                        var pOdd = (double*)(pData + k + j + m2);

                        var vEven = Avx.LoadVector256(pEven);
                        var vOdd = Avx.LoadVector256(pOdd);

                        var vTwiddle = MultiplyComplexAvx(vOdd, vW);

                        Avx.Store(pEven, Avx.Add(vEven, vTwiddle));
                        Avx.Store(pOdd, Avx.Subtract(vEven, vTwiddle));

                        vW = MultiplyComplexAvx(vW, vWmStep);
                    }
                }
            }
        }
    }

    /// <summary> Helper for a single FFT stage using SSE, callable from other FFT methods. </summary>
    private static unsafe void FftSseStage(Complex* pData, int n, int m)
    {
        var m2 = m >> 1;
        var wMAngle = -2.0 * Math.PI / m;
        var wMComplex = Complex.FromPolarCoordinates(1.0, wMAngle);
        var vWm = Vector128.Create(wMComplex.Real, wMComplex.Imaginary);

        for (var k = 0; k < n; k += m)
        {
            var vW = Vector128.Create(1.0, 0.0);
            for (var j = 0; j < m2; j++)
            {
                var pEven = (double*)(pData + k + j);
                var pOdd = (double*)(pData + k + j + m2);

                var vEven = Sse2.LoadVector128(pEven);
                var vOdd = Sse2.LoadVector128(pOdd);

                var vTwiddle = MultiplyComplexSse3(vOdd, vW);

                Sse2.Store(pEven, Sse2.Add(vEven, vTwiddle));
                Sse2.Store(pOdd, Sse2.Subtract(vEven, vTwiddle));

                vW = MultiplyComplexSse3(vW, vWm);
            }
        }
    }

    /// <summary>
    /// Bit-reverses the order of elements in a complex array.
    /// </summary>
    /// <param name="data">The complex data array. Must be a power of 2 in length.</param>
    private static void BitReverse(Complex[] data)
    {
        var n = data.Length;
        var j = 0;
        for (var i = 1; i < n; i++)
        {
            var bit = n >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }

            j ^= bit;

            if (i < j)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }
    }

    /// <summary>
    /// Multiplies a complex number by another using SSE3.
    /// a * b = (ax*bx - ay*by, ax*by + ay*bx)
    /// </summary>
    private static Vector128<double> MultiplyComplexSse3(Vector128<double> a, Vector128<double> b)
    {
        var realA = Sse2.UnpackLow(a, a); // [ax, ax]
        var imagA = Sse2.UnpackHigh(a, a); // [ay, ay]
        var bShuffled = Sse2.Shuffle(b, b, 1); // [by, bx]

        var term1 = Sse2.Multiply(realA, b); // [ax*bx, ax*by]
        var term2 = Sse2.Multiply(imagA, bShuffled); // [ay*by, ay*bx]

        // Returns [term1_low - term2_low, term1_high + term2_high]
        return Sse3.AddSubtract(term1, term2);
    }

    /// <summary>
    /// Multiplies two pairs of complex numbers using AVX.
    /// </summary>
    private static Vector256<double> MultiplyComplexAvx(Vector256<double> a, Vector256<double> b)
    {
        // bShuffled = [b0.im, b0.re, b1.im, b1.re]
        var bShuffled = Avx.Shuffle(b, b, 0b0101);
        // aReal = [a0.re, a0.re, a1.re, a1.re]
        var aReal = Avx.Shuffle(a, a, 0b0000);
        // aImag = [a0.im, a0.im, a1.im, a1.im]
        var aImag = Avx.Shuffle(a, a, 0b1111);

        var term1 = Avx.Multiply(aReal, b); // [a0r*b0r, a0r*b0i, a1r*b1r, a1r*b1i]
        var term2 = Avx.Multiply(aImag, bShuffled); // [a0i*b0i, a0i*b0r, a1i*b1i, a1i*b1r]

        // Returns [t1-t2, t1+t2, t1-t2, t1+t2] for corresponding elements
        return Avx.AddSubtract(term1, term2);
    }

    /// <summary>
    /// Generates a Hamming window of a specified size.
    /// </summary>
    /// <param name="size">The size of the Hamming window.</param>
    /// <returns>The Hamming window array.</returns>
    public static float[] HammingWindow(int size)
    {
        if (size <= 0) return [];
        if (size == 1) return [1.0f];

        // SSE4.1 is required for the fast cosine approximation's Floor intrinsic
        if (EnableAvx && Avx.IsSupported && size >= Vector256<float>.Count)
            return HammingWindowAvx(size);
        if (EnableSse && Sse41.IsSupported && size >= Vector128<float>.Count)
            return HammingWindowSse(size);

        return HammingWindowScalar(size);
    }

    /// <summary>
    /// Generates a Hamming window using a scalar implementation.
    /// </summary>
    private static float[] HammingWindowScalar(int size)
    {
        var window = new float[size];
        var factor = 2 * MathF.PI / (size - 1);
        for (var i = 0; i < size; i++)
        {
            window[i] = 0.54f - 0.46f * MathF.Cos(i * factor);
        }

        return window;
    }

    /// <summary>
    /// Generates a Hamming window using SSE acceleration.
    /// </summary>
    private static unsafe float[] HammingWindowSse(int size)
    {
        var window = new float[size];
        var vectorSize = Vector128<float>.Count;
        var mainLoopSize = size - (size % vectorSize);

        fixed (float* pWindow = window)
        {
            var vConstA = Vector128.Create(0.54f);
            var vConstB = Vector128.Create(0.46f);
            var vFactor = Vector128.Create(2.0f * MathF.PI / (size - 1));
            var vIndicesBase = Vector128.Create(0f, 1f, 2f, 3f);

            for (var i = 0; i < mainLoopSize; i += vectorSize)
            {
                var vI = Vector128.Create((float)i);
                var vIndices = Sse.Add(vI, vIndicesBase);
                var vCosArg = Sse.Multiply(vFactor, vIndices);
                var vCos = FastCosineSse(vCosArg);
                var vResult = Sse.Subtract(vConstA, Sse.Multiply(vConstB, vCos));
                Sse.Store(pWindow + i, vResult);
            }
        }

        // Handle remaining elements with scalar logic
        var scalarFactor = 2 * MathF.PI / (size - 1);
        for (var i = mainLoopSize; i < size; i++)
        {
            window[i] = 0.54f - 0.46f * MathF.Cos(i * scalarFactor);
        }

        return window;
    }

    /// <summary>
    /// Generates a Hamming window using AVX acceleration.
    /// </summary>
    private static unsafe float[] HammingWindowAvx(int size)
    {
        var window = new float[size];
        var vectorSize = Vector256<float>.Count;
        var mainLoopSize = size - (size % vectorSize);

        fixed (float* pWindow = window)
        {
            var vConstA = Vector256.Create(0.54f);
            var vConstB = Vector256.Create(0.46f);
            var vFactor = Vector256.Create(2.0f * MathF.PI / (size - 1));
            var vIndicesBase = Vector256.Create(0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f);

            for (var i = 0; i < mainLoopSize; i += vectorSize)
            {
                var vI = Vector256.Create((float)i);
                var vIndices = Avx.Add(vI, vIndicesBase);
                var vCosArg = Avx.Multiply(vFactor, vIndices);
                var vCos = FastCosineAvx(vCosArg);
                var vResult = Avx.Subtract(vConstA, Avx.Multiply(vConstB, vCos));
                Avx.Store(pWindow + i, vResult);
            }
        }

        var scalarFactor = 2 * MathF.PI / (size - 1);
        for (var i = mainLoopSize; i < size; i++)
        {
            window[i] = 0.54f - 0.46f * MathF.Cos(i * scalarFactor);
        }

        return window;
    }

    /// <summary>
    /// Generates a Hanning window of a specified size.
    /// </summary>
    public static float[] HanningWindow(int size)
    {
        if (size <= 0) return [];
        if (size == 1) return [1.0f];

        if (EnableAvx && Avx.IsSupported && size >= Vector256<float>.Count)
            return HanningWindowAvx(size);
        if (EnableSse && Sse41.IsSupported && size >= Vector128<float>.Count)
            return HanningWindowSse(size);

        return HanningWindowScalar(size);
    }

    /// <summary>
    /// Generates a Hanning window using a scalar implementation.
    /// </summary>
    private static float[] HanningWindowScalar(int size)
    {
        var window = new float[size];
        var factor = 2 * MathF.PI / (size - 1);
        for (var i = 0; i < size; i++)
        {
            window[i] = 0.5f * (1.0f - MathF.Cos(i * factor));
        }

        return window;
    }

    /// <summary>
    /// Generates a Hanning window using SSE acceleration.
    /// </summary>
    private static unsafe float[] HanningWindowSse(int size)
    {
        var window = new float[size];
        var vectorSize = Vector128<float>.Count;
        var mainLoopSize = size - (size % vectorSize);

        fixed (float* pWindow = window)
        {
            var vConstA = Vector128.Create(0.5f);
            var vFactor = Vector128.Create(2.0f * MathF.PI / (size - 1));
            var vIndicesBase = Vector128.Create(0f, 1f, 2f, 3f);

            for (var i = 0; i < mainLoopSize; i += vectorSize)
            {
                var vI = Vector128.Create((float)i);
                var vIndices = Sse.Add(vI, vIndicesBase);
                var vCosArg = Sse.Multiply(vFactor, vIndices);
                var vCos = FastCosineSse(vCosArg);
                var vResult = Sse.Multiply(vConstA, Sse.Subtract(Vector128.Create(1.0f), vCos));
                Sse.Store(pWindow + i, vResult);
            }
        }

        var scalarFactor = 2 * MathF.PI / (size - 1);
        for (var i = mainLoopSize; i < size; i++)
        {
            window[i] = 0.5f * (1.0f - MathF.Cos(i * scalarFactor));
        }

        return window;
    }

    /// <summary>
    /// Generates a Hanning window using AVX acceleration.
    /// </summary>
    private static unsafe float[] HanningWindowAvx(int size)
    {
        var window = new float[size];
        var vectorSize = Vector256<float>.Count;
        var mainLoopSize = size - (size % vectorSize);

        fixed (float* pWindow = window)
        {
            var vConstA = Vector256.Create(0.5f);
            var vFactor = Vector256.Create(2.0f * MathF.PI / (size - 1));
            var vIndicesBase = Vector256.Create(0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f);

            for (var i = 0; i < mainLoopSize; i += vectorSize)
            {
                var vI = Vector256.Create((float)i);
                var vIndices = Avx.Add(vI, vIndicesBase);
                var vCosArg = Avx.Multiply(vFactor, vIndices);
                var vCos = FastCosineAvx(vCosArg);
                var vResult = Avx.Multiply(vConstA, Avx.Subtract(Vector256.Create(1.0f), vCos));
                Avx.Store(pWindow + i, vResult);
            }
        }

        var scalarFactor = 2 * MathF.PI / (size - 1);
        for (var i = mainLoopSize; i < size; i++)
        {
            window[i] = 0.5f * (1.0f - MathF.Cos(i * scalarFactor));
        }

        return window;
    }

    /// <summary>
    /// Performs linear interpolation between two values
    /// </summary>
    public static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);

    /// <summary>
    /// Checks if a number is a power of two (2, 4, 8, 16, etc.).
    /// </summary>
    public static bool IsPowerOfTwo(long n) => (n > 0) && ((n & (n - 1)) == 0);

    /// <summary>
    /// Returns the remainder after division, in the range [0, y).
    /// </summary>
    public static double Mod(this double x, double y) => x - y * Math.Floor(x / y);

    /// <summary>
    /// Returns the principal angle of a number in the range [-PI, PI).
    /// </summary>
    public static float PrincipalAngle(float angle)
    {
        return angle - (2 * MathF.PI * MathF.Floor((angle + MathF.PI) / (2 * MathF.PI)));
    }

    /// <summary>
    /// Approximates the cosine of a vector using a highly accurate polynomial on a reduced quadrant.
    /// Requires SSE4.1 for Floor/BlendVariable.
    /// </summary>
    private static Vector128<float> FastCosineSse(Vector128<float> x)
    {
        var vInv2Pi = Vector128.Create(1.0f / (2 * MathF.PI));
        var v2Pi = Vector128.Create(2 * MathF.PI);
        var vHalf = Vector128.Create(0.5f);
        var vPi = Vector128.Create(MathF.PI);
        var vPiHalf = Vector128.Create(MathF.PI / 2.0f);
        var signMaskAbs = Vector128.Create(0x7FFFFFFF).AsSingle();
        var signMaskFlip = Vector128.Create(-0.0f).AsSingle();

        var n = Sse41.Floor(Sse.Add(Sse.Multiply(x, vInv2Pi), vHalf));
        x = Sse.Subtract(x, Sse.Multiply(n, v2Pi));

        var absX = Sse.And(x, signMaskAbs);
        var q2Mask = Sse.CompareGreaterThan(absX, vPiHalf);
        var signFlip = Sse.And(q2Mask, signMaskFlip);
        x = Sse41.BlendVariable(absX, Sse.Subtract(vPi, absX), q2Mask);

        var xSquared = Sse.Multiply(x, x);
        var c0 = Vector128.Create(1.0f);
        var c1 = Vector128.Create(-0.49999997f);
        var c2 = Vector128.Create(0.0416666f);
        var c3 = Vector128.Create(-0.00138887f);
        var c4 = Vector128.Create(2.47977e-5f);
        var c5 = Vector128.Create(-2.62134e-7f);

        var poly = Sse.Add(Sse.Multiply(c5, xSquared), c4);
        poly = Sse.Add(Sse.Multiply(poly, xSquared), c3);
        poly = Sse.Add(Sse.Multiply(poly, xSquared), c2);
        poly = Sse.Add(Sse.Multiply(poly, xSquared), c1);
        poly = Sse.Add(Sse.Multiply(poly, xSquared), c0);

        return Sse.Xor(poly, signFlip);
    }

    /// <summary>
    /// Approximates the cosine of a vector using a highly accurate polynomial on a reduced quadrant.
    /// </summary>
    private static Vector256<float> FastCosineAvx(Vector256<float> x)
    {
        var vInv2Pi = Vector256.Create(1.0f / (2 * MathF.PI));
        var v2Pi = Vector256.Create(2 * MathF.PI);
        var vHalf = Vector256.Create(0.5f);
        var vPi = Vector256.Create(MathF.PI);
        var vPiHalf = Vector256.Create(MathF.PI / 2.0f);
        var signMaskAbs = Vector256.Create(0x7FFFFFFF).AsSingle();
        var signMaskFlip = Vector256.Create(-0.0f).AsSingle();

        var n = Avx.Floor(Avx.Add(Avx.Multiply(x, vInv2Pi), vHalf));
        x = Avx.Subtract(x, Avx.Multiply(n, v2Pi));

        var absX = Avx.And(x, signMaskAbs);
        var q2Mask = Avx.Compare(absX, vPiHalf, FloatComparisonMode.OrderedGreaterThanNonSignaling);
        var signFlip = Avx.And(q2Mask, signMaskFlip);
        x = Avx.BlendVariable(absX, Avx.Subtract(vPi, absX), q2Mask);

        var xSquared = Avx.Multiply(x, x);
        var c0 = Vector256.Create(1.0f);
        var c1 = Vector256.Create(-0.49999997f);
        var c2 = Vector256.Create(0.0416666f);
        var c3 = Vector256.Create(-0.00138887f);
        var c4 = Vector256.Create(2.47977e-5f);
        var c5 = Vector256.Create(-2.62134e-7f);

        var poly = Avx.Add(Avx.Multiply(c5, xSquared), c4);
        poly = Avx.Add(Avx.Multiply(poly, xSquared), c3);
        poly = Avx.Add(Avx.Multiply(poly, xSquared), c2);
        poly = Avx.Add(Avx.Multiply(poly, xSquared), c1);
        poly = Avx.Add(Avx.Multiply(poly, xSquared), c0);

        return Avx.Xor(poly, signFlip);
    }
}