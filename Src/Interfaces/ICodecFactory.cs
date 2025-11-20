using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Interfaces;

/// <summary>
/// Defines a factory for creating custom <see cref="ISoundDecoder"/> and <see cref="ISoundEncoder"/> instances.
/// Implement this interface to add support for new audio formats or to provide alternative implementations
/// for existing formats.
/// </summary>
public interface ICodecFactory
{
    /// <summary>
    /// Gets a unique, lowercase string identifier for this factory implementation. It is recommended to use
    /// a namespace-qualified name to avoid collisions, e.g., "mycompany.customflacfactory".
    /// This ID is used for unregistering and re-prioritizing codecs.
    /// </summary>
    string FactoryId { get; }
    
    /// <summary>
    /// Gets a collection of unique, lowercase string identifiers for the audio formats this factory supports.
    /// For example, a factory for FFmpeg might return ["mp3", "aac", "ogg", "opus"].
    /// A factory for a specific library would return a single format, like ["flac"].
    /// </summary>
    IReadOnlyCollection<string> SupportedFormatIds { get; }

    /// <summary>
    /// Gets the default priority of this factory. This priority is used upon initial registration but can be
    /// overridden at runtime using <see cref="AudioEngine.SetCodecPriority"/>. When multiple factories are
    /// registered for the same format, the one with the highest priority number will be tried first.
    /// Built-in engine codecs use a low priority (e.g., 0).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Creates a new decoder instance for one of the supported formats.
    /// </summary>
    /// <param name="stream">The input stream containing the audio data.</param>
    /// <param name="formatId">The specific format identifier being requested (e.g., "mp3", "flac"). This allows a multi-format factory to know which decoder to create.</param>
    /// <param name="format">The audio format parameters discovered by the engine.</param>
    /// <returns>A valid <see cref="ISoundDecoder"/> instance on success, or <c>null</c> if this factory fails to create a decoder for the given stream.</returns>
    ISoundDecoder? CreateDecoder(Stream stream, string formatId, AudioFormat format);

    /// <summary>
    /// Attempts to create a decoder by probing the given stream directly.
    /// This method is called by the engine when the audio format is unknown.
    /// </summary>
    /// <param name="stream">The input stream containing audio data. The stream must be seekable.</param>
    /// <param name="detectedFormat">When this method returns, contains the audio format detected by the decoder if successful.</param>
    /// <param name="hintFormat">An optional hint for the desired output audio format. The factory should attempt to produce this format if possible.</param>
    /// <returns>A valid <see cref="ISoundDecoder"/> instance on success, or <c>null</c> if the factory cannot handle the stream.</returns>
    ISoundDecoder? TryCreateDecoder(Stream stream, out AudioFormat detectedFormat, AudioFormat? hintFormat = null);

    /// <summary>
    /// Creates a new encoder instance for one of the supported formats.
    /// </summary>
    /// <param name="stream">The output stream to write encoded audio to.</param>
    /// <param name="formatId">The specific format identifier being requested (e.g., "wav").</param>
    /// <param name="format">The audio format parameters for the raw audio to be encoded.</param>
    /// <returns>A valid <see cref="ISoundEncoder"/> instance on success, or <c>null</c> if encoding is not supported or fails.</returns>
    ISoundEncoder? CreateEncoder(Stream stream, string formatId, AudioFormat format);
}