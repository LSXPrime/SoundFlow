namespace SoundFlow.Synthesis.Instruments;

/// <summary>
/// Represents a synthesizer instrument or patch.
/// It contains the logic to select a voice definition based on note and velocity,
/// supporting multi-sampling, key splits, and velocity layers.
/// </summary>
public class Instrument
{
    private readonly List<VoiceMapping> _voiceMappings;
    private readonly VoiceDefinition _fallbackDefinition;

    /// <summary>
    /// Gets a value indicating whether this instrument is a fallback placeholder.
    /// </summary>
    public bool IsFallback { get; }
    
    /// <summary>
    /// Gets a value indicating whether this instrument has no mappings.
    /// </summary>
    public bool IsEmpty => _voiceMappings.Count == 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="Instrument"/> class.
    /// </summary>
    /// <param name="mappings">A list of voice mappings that define the instrument's layers and splits.</param>
    /// <param name="fallbackDefinition">A default voice definition to use if no other mapping matches.</param>
    /// <param name="isFallback">Whether this instrument should be considered a fallback placeholder.</param>
    public Instrument(List<VoiceMapping> mappings, VoiceDefinition fallbackDefinition, bool isFallback = false)
    {
        _voiceMappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        _fallbackDefinition = fallbackDefinition ?? throw new ArgumentNullException(nameof(fallbackDefinition));
        IsFallback = isFallback;
    }

    /// <summary>
    /// Gets the appropriate voice definition for a given note number and velocity.
    /// It iterates through the instrument's mappings to find the first one that matches
    /// the input note and velocity.
    /// </summary>
    /// <param name="noteNumber">The MIDI note number.</param>
    /// <param name="velocity">The note's velocity.</param>
    /// <returns>The selected <see cref="VoiceDefinition"/>, or a fallback definition if no specific mapping is found.</returns>
    public VoiceDefinition GetVoiceDefinition(int noteNumber, int velocity)
    {
        // Find the first mapping that matches the incoming note and velocity.
        foreach (var mapping in _voiceMappings)
        {
            if (mapping.IsMatch(noteNumber, velocity))
            {
                return mapping.Definition;
            }
        }

        // If no specific mapping was found, return the fallback definition for this instrument.
        return _fallbackDefinition;
    }
}