namespace SoundFlow.Editing;

/// <summary>
/// Defines the types of curves that can be used for fade-in and fade-out effects on audio segments.
/// </summary>
public enum FadeCurveType
{
    /// <summary>
    /// A linear fade, where the volume changes at a constant rate.
    /// </summary>
    Linear,

    /// <summary>
    /// A logarithmic fade, where the volume changes more rapidly at the beginning/end of the fade
    /// and more slowly in the middle, to better match human perception of loudness.
    /// </summary>
    Logarithmic,

    /// <summary>
    /// An S-shaped fade curve, providing a smooth transition in and out of the fade.
    /// Also known as EaseInOutSine or Smoothstep.
    /// </summary>
    SCurve
}
