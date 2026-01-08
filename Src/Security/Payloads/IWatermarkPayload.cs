using System.Collections;

namespace SoundFlow.Security.Payloads;

/// <summary>
/// Defines the contract for data that can be embedded into an audio watermark.
/// </summary>
public interface IWatermarkPayload
{
    /// <summary>
    /// Converts the high-level payload data into a bit array for embedding.
    /// </summary>
    /// <returns>A <see cref="BitArray"/> representing the payload.</returns>
    BitArray ToBits();

    /// <summary>
    /// Reconstructs the high-level payload data from a bit array.
    /// </summary>
    /// <param name="bits">The extracted bits.</param>
    /// <returns>The reconstructed object (e.g., a string or byte array).</returns>
    object? FromBits(BitArray bits);
}