namespace SoundFlow.Synthesis.Instruments;

/// <summary>
/// Defines a mapping that links a specific range of MIDI notes and velocities
/// to a particular VoiceDefinition. This is the core of a multi-sampled instrument.
/// </summary>
public class VoiceMapping
{
    /// <summary>
    /// The VoiceDefinition to use when the conditions of this mapping are met.
    /// </summary>
    public VoiceDefinition Definition { get; }

    /// <summary>
    /// The minimum MIDI note number for this mapping (inclusive).
    /// </summary>
    public int MinKey { get; set; }

    /// <summary>
    /// The maximum MIDI note number for this mapping (inclusive).
    /// </summary>
    public int MaxKey { get; set; } = 127;

    /// <summary>
    /// The minimum velocity for this mapping (inclusive).
    /// </summary>
    public int MinVelocity { get; set; }

    /// <summary>
    /// The maximum velocity for this mapping (inclusive).
    /// </summary>
    public int MaxVelocity { get; set; } = 127;
    
    /// <summary>
    /// Gets or sets the initial volume attenuation applied to the voice (in dB).
    /// </summary>
    public float InitialAttenuation { get; set; } // in dB
    
    /// <summary>
    /// Gets or sets the stereo pan position for the voice (-1.0 for hard left, 1.0 for hard right, 0.0 for center).
    /// </summary>
    public float Pan { get; set; } // -1 to 1
    
    /// <summary>
    /// Gets or sets the MIDI key number that should be treated as the root or natural pitch of the sample.
    /// A value of -1 indicates that the sample's inherent root key should be used.
    /// </summary>
    public int RootKeyOverride { get; set; } = -1;
    
    /// <summary>
    /// Gets or sets the fine tuning offset applied to the voice (in cents).
    /// </summary>
    public int Tune { get; set; } // in cents
    
    /// <summary>
    /// Gets or sets the sample looping behavior (e.g., non-looping, continuous loop).
    /// The exact interpretation depends on the synthesizer engine.
    /// </summary>
    public int LoopMode { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VoiceMapping"/> class.
    /// </summary>
    /// <param name="definition">The VoiceDefinition for this mapping.</param>
    public VoiceMapping(VoiceDefinition definition)
    {
        Definition = definition;
    }

    /// <summary>
    /// Checks if a given note number and velocity fall within this mapping's range.
    /// </summary>
    /// <param name="noteNumber">The MIDI note number to check.</param>
    /// <param name="velocity">The velocity to check.</param>
    /// <returns>True if the note and velocity are within the defined ranges.</returns>
    public bool IsMatch(int noteNumber, int velocity)
    {
        return noteNumber >= MinKey && noteNumber <= MaxKey &&
               velocity >= MinVelocity && velocity <= MaxVelocity;
    }
}