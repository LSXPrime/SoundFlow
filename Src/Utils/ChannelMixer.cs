using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace SoundFlow.Utils;

/// <summary>
/// A high-performance utility class for mixing audio channels.
/// Assumes audio data is in an interleaved format (e.g., [L, R, L, R, ...]).
/// </summary>
public static class ChannelMixer
{
    /// <summary>
    /// Gets or sets a value indicating whether to use AVX (Advanced Vector Extensions) instructions
    /// if the hardware supports them. Defaults to <c>true</c>.
    /// </summary>
    public static bool EnableAvx { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to use SSE (Streaming SIMD Extensions) instructions
    /// if the hardware supports them. Defaults to <c>true</c>.
    /// </summary>
    public static bool EnableSse { get; set; } = true;

    /// <summary>
    /// Mixes an audio sample buffer from a source channel count to a target channel count.
    /// </summary>
    /// <param name="samples">The float array of audio samples, in interleaved format.</param>
    /// <param name="sourceChannels">The number of channels in the source audio.</param>
    /// <param name="targetChannels">The desired number of channels for the output audio.</param>
    /// <returns>A new float array with the mixed audio samples.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the samples array is null.</exception>
    /// <exception cref="ArgumentException">Thrown for invalid arguments, such as non-positive channel counts or an array length that is not a multiple of the source channel count.</exception>
    public static float[] Mix(float[] samples, int sourceChannels, int targetChannels)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (sourceChannels <= 0 || targetChannels <= 0)
            throw new ArgumentException("Source and target channel counts must be greater than zero.");

        if (samples.Length % sourceChannels != 0)
            throw new ArgumentException(
                "The length of the samples array is not a valid multiple of the source channel count.",
                nameof(samples));

        if (sourceChannels == targetChannels)
            return (float[])samples.Clone(); // No mixing needed, return a copy.

        if (samples.Length == 0)
            return [];

        return sourceChannels switch
        {
            1 when targetChannels == 2 => MixMonoToStereo(samples),
            2 when targetChannels == 1 => MixStereoToMono(samples),
            _ => MixGeneral(samples, sourceChannels, targetChannels)
        };
    }

    /// <summary>
    /// Mixes a mono signal to stereo by duplicating the mono sample for both left and right channels.
    /// </summary>
    private static unsafe float[] MixMonoToStereo(float[] source)
    {
        var frameCount = source.Length;
        var dest = new float[frameCount * 2];
        var mainLoopFrames = 0;

        fixed (float* pSource = source, pDest = dest)
        {
            // AVX path can process 8 mono samples into 8 stereo pairs per iteration.
            if (EnableAvx && Avx.IsSupported && frameCount >= 8)
            {
                mainLoopFrames = frameCount - (frameCount % 8);
                for (var i = 0; i < mainLoopFrames; i += 8)
                {
                    var vSource = Avx.LoadVector256(pSource + i); // [m0, m1, m2, m3, m4, m5, m6, m7]

                    // Unpack to interleave samples
                    var vLow = Avx.UnpackLow(vSource, vSource);   // [m0, m0, m1, m1, m4, m4, m5, m5]
                    var vHigh = Avx.UnpackHigh(vSource, vSource); // [m2, m2, m3, m3, m6, m6, m7, m7]

                    // Permute to correct order [m0,m0, m1,m1, m2,m2, m3,m3]
                    var vResult1 = Avx.Permute2x128(vLow, vHigh, 0b0010_0000);
                    // Permute to correct order [m4,m4, m5,m5, m6,m6, m7,m7]
                    var vResult2 = Avx.Permute2x128(vLow, vHigh, 0b0011_0001);

                    Avx.Store(pDest + (i * 2), vResult1);
                    Avx.Store(pDest + (i * 2) + 8, vResult2);
                }
            }
            // SSE path can process 4 mono samples into 4 stereo pairs per iteration.
            else if (EnableSse && Sse.IsSupported && frameCount >= 4)
            {
                mainLoopFrames = frameCount - (frameCount % 4);
                for (var i = 0; i < mainLoopFrames; i += 4)
                {
                    var vSource = Sse.LoadVector128(pSource + i); // [m0, m1, m2, m3]
                        
                    // vLow = [m0, m0, m1, m1]
                    var vLow = Sse.UnpackLow(vSource, vSource);
                    // vHigh = [m2, m2, m3, m3]
                    var vHigh = Sse.UnpackHigh(vSource, vSource);

                    Sse.Store(pDest + (i * 2), vLow);
                    Sse.Store(pDest + (i * 2) + 4, vHigh);
                }
            }
        }

        // Scalar fallback for remaining frames or if SIMD is disabled/unsupported.
        for (var i = mainLoopFrames; i < frameCount; i++)
        {
            var monoSample = source[i];
            dest[i * 2] = monoSample;     // Left channel
            dest[i * 2 + 1] = monoSample; // Right channel
        }

        return dest;
    }

    /// <summary>
    /// Mixes a stereo signal to mono by averaging the left and right channels.
    /// </summary>
    private static unsafe float[] MixStereoToMono(float[] source)
    {
        var frameCount = source.Length / 2;
        var dest = new float[frameCount];
        var mainLoopFrames = 0;

        fixed (float* pSource = source, pDest = dest)
        {
            // AVX2 path can process 4 stereo pairs more efficiently by avoiding lane crossing.
            if (EnableAvx && Avx2.IsSupported && frameCount >= 4)
            {
                mainLoopFrames = frameCount - (frameCount % 4);
                var vHalf = Vector256.Create(0.5f);
                // Mask to gather elements from indices [0, 1, 4, 5] (the sums) into the first 4 slots.
                var vPermuteMask = Vector256.Create(0, 1, 4, 5, 0, 0, 0, 0);

                for (var i = 0; i < mainLoopFrames; i += 4)
                {
                    // Load 4 stereo pairs (8 floats)
                    var vSource = Avx.LoadVector256(pSource + (i * 2)); // [L0, R0, L1, R1, L2, R2, L3, R3]
                    // Sum adjacent pairs horizontally: vSum = [L0+R0, L1+R1, _, _, L2+R2, L3+R3, _, _]
                    var vSum = Avx.HorizontalAdd(vSource, vSource);
                    // Average by multiplying by 0.5. The results are at indices 0, 1, 4, 5
                    var vAvg = Avx.Multiply(vSum, vHalf); // [avg0, avg1, junk, junk, avg2, avg3, junk, junk]
                    // Use permute to gather the 4 desired results into one 128-bit vector.
                    var vResult = Avx2.PermuteVar8x32(vAvg.AsSingle(), vPermuteMask);
                    // Store the final result: [avg0, avg1, avg2, avg3]
                    Sse.Store(pDest + i, vResult.GetLower());
                }
            }
            // AVX path can process 4 stereo pairs into 4 mono samples per iteration.
            else if (EnableAvx && Avx.IsSupported && frameCount >= 4)
            {
                mainLoopFrames = frameCount - (frameCount % 4);
                var vHalf = Vector256.Create(0.5f);
                for (var i = 0; i < mainLoopFrames; i += 4)
                {
                    // Load 4 stereo pairs (8 floats)
                    var vSource = Avx.LoadVector256(pSource + (i * 2)); // [L0, R0, L1, R1, L2, R2, L3, R3]
                    // Sum adjacent pairs horizontally: vSum = [L0+R0, L1+R1, _, _, L2+R2, L3+R3, _, _]
                    var vSum = Avx.HorizontalAdd(vSource, vSource);
                    // Average by multiplying by 0.5
                    var vAvg = Avx.Multiply(vSum, vHalf);
                    // The desired results are in the lower 128-bit lane and upper 128-bit lane.
                    var vLow = vAvg.GetLower();  // [avg0, avg1, junk, junk]
                    var vHigh = vAvg.GetUpper(); // [avg2, avg3, junk, junk]
                    // Shuffle combines the results from the two lanes. Mask 0x44 selects [low0, low1, high0, high1].
                    var vResult = Sse.Shuffle(vLow, vHigh, 0x44); // Result: [avg0, avg1, avg2, avg3]
                    // Store the combined 128-bit result (4 floats)
                    Sse.Store(pDest + i, vResult);
                }
            }
            // SSE3 path can process 2 stereo pairs into 2 mono samples per iteration.
            else if (EnableSse && Sse3.IsSupported && frameCount >= 2)
            {
                mainLoopFrames = frameCount - (frameCount % 2);
                var vHalf = Vector128.Create(0.5f);
                for (var i = 0; i < mainLoopFrames; i += 2)
                {
                    // Load 2 stereo pairs (4 floats)
                    var vSource = Sse.LoadVector128(pSource + (i * 2)); // [L0, R0, L1, R1]
                    // Sum adjacent pairs horizontally
                    var vSum = Sse3.HorizontalAdd(vSource, vSource); // [L0+R0, L1+R1, L0+R0, L1+R1]
                    // Average by multiplying by 0.5
                    var vAvg = Sse.Multiply(vSum, vHalf);
                    // Store the first 2 results (64-bit)
                    *(long*)(pDest + i) = vAvg.AsInt64().GetElement(0);
                }
            }
        }

        // Scalar fallback for remaining frames or if SIMD is disabled/unsupported.
        for (var i = mainLoopFrames; i < frameCount; i++)
        {
            // Average the two channels. Multiplying by 0.5 is faster than dividing.
            dest[i] = (source[i * 2] + source[i * 2 + 1]) * 0.5f;
        }

        return dest;
    }

    /// <summary>
    /// General-purpose mixer using a single-pass, SIMD-accelerated approach.
    /// This implementation is cache-friendly and avoids intermediate buffers.
    /// </summary>
    private static unsafe float[] MixGeneral(float[] samples, int sourceChannels, int targetChannels)
    {
        var frameCount = samples.Length / sourceChannels;
        var destSamples = new float[frameCount * targetChannels];
        var invSourceChannels = 1.0f / sourceChannels;

        fixed (float* pSource = samples, pDest = destSamples)
        {
            for (var i = 0; i < frameCount; i++)
            {
                var sourceFrameIndex = i * sourceChannels;
                var destFrameIndex = i * targetChannels;
                
                // a) Downmix: Calculate the sum of all source channels for this frame using SIMD.
                var monoSampleSum = 0.0f;
                var ch = 0;

                if (EnableAvx && Avx.IsSupported && sourceChannels >= 8)
                {
                    var vSum = Vector256<float>.Zero;
                    var mainLoopChannels = sourceChannels - (sourceChannels % 8);
                    for (; ch < mainLoopChannels; ch += 8)
                    {
                        // Load 8 source channels for this frame
                        var vSource = Avx.LoadVector256(pSource + sourceFrameIndex + ch); // [c0, c1, c2, c3, c4, c5, c6, c7]
                        vSum = Avx.Add(vSum, vSource);
                    }
                    monoSampleSum += HorizontalSum(vSum);
                }
                
                if (EnableSse && Sse.IsSupported && sourceChannels - ch >= 4)
                {
                    var vSum = Vector128<float>.Zero;
                    var mainLoopChannels = sourceChannels - (sourceChannels % 4);
                    for (; ch < mainLoopChannels; ch += 4)
                    {
                        // Load 4 source channels for this frame
                        var vSource = Sse.LoadVector128(pSource + sourceFrameIndex + ch); // [c0, c1, c2, c3]
                        vSum = Sse.Add(vSum, vSource);
                    }
                    monoSampleSum += HorizontalSum(vSum);
                }
                
                // Scalar sum for any remaining channels.
                for (; ch < sourceChannels; ch++)
                {
                    monoSampleSum += pSource[sourceFrameIndex + ch];
                }

                var monoSample = monoSampleSum * invSourceChannels;

                // b) Upmix: Write the mono sample to all target channels using SIMD broadcast.
                ch = 0;
                
                if (EnableAvx && Avx.IsSupported && targetChannels >= 8)
                {
                    // Create a vector where all 8 elements are the mono sample
                    var vBroadcast = Vector256.Create(monoSample);
                    var mainLoopChannels = targetChannels - (targetChannels % 8);
                    for (; ch < mainLoopChannels; ch += 8)
                    {
                        Avx.Store(pDest + destFrameIndex + ch, vBroadcast);
                    }
                }

                if (EnableSse && Sse.IsSupported && targetChannels - ch >= 4)
                {
                    // Create a vector where all 4 elements are the mono sample
                    var vBroadcast = Vector128.Create(monoSample);
                    var mainLoopChannels = targetChannels - (targetChannels % 4);
                    for (; ch < mainLoopChannels; ch += 4)
                    {
                        Sse.Store(pDest + destFrameIndex + ch, vBroadcast);
                    }
                }
                
                // Scalar write for any remaining channels.
                for (; ch < targetChannels; ch++)
                {
                    pDest[destFrameIndex + ch] = monoSample;
                }
            }
        }

        return destSamples;
    }
    
    /// <summary>
    /// Calculates the horizontal sum of all elements in a 128-bit vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float HorizontalSum(Vector128<float> v)
    {
        // v = [x, y, z, w]
        var high = Sse.MoveHighToLow(v, v);                    // high      = [z, w, z, w]
        var sumLanes = Sse.Add(v, high);                      // sum_lanes = [x+z, y+w, z+z, w+w]
        var shuffle = Sse.Shuffle(sumLanes, sumLanes, 0b01_01_01_01); // shuffle   = [y+w, y+w, y+w, y+w]
        var sumTotal = Sse.Add(sumLanes, shuffle);           // sum_total = [x+z+y+w, ...]
        return sumTotal.GetElement(0);
    }
    
    /// <summary>
    /// Calculates the horizontal sum of all elements in a 256-bit vector.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float HorizontalSum(Vector256<float> v)
    {
        // Sum the upper and lower 128-bit lanes together and then sum that result.
        return HorizontalSum(v.GetLower() + v.GetUpper());
    }
}