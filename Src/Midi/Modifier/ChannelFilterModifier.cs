using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Structs;
using SoundFlow.Structs;

namespace SoundFlow.Midi.Modifier;

/// <summary>
/// A MIDI modifier that filters messages based on their channel.
/// </summary>
public sealed class ChannelFilterModifier : MidiModifier
{
    /// <inheritdoc />
    public override string Name => $"Channel Filter ({Channel})";
    
    /// <summary>
    /// Gets or sets the MIDI channel to allow. Messages on other channels will be dropped.
    /// </summary>
    public int Channel { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelFilterModifier"/> class.
    /// </summary>
    /// <param name="channel">The MIDI channel (1-16) to allow.</param>
    public ChannelFilterModifier(int channel)
    {
        Channel = channel;
    }

    /// <inheritdoc />
    public override IEnumerable<MidiMessage> Process(MidiMessage message)
    {
        if (!IsEnabled)
        {
            yield return message;
            yield break;
        }

        if (message.Channel == Channel)
        {
            yield return message;
        }
        // If the channel does not match, returning nothing effectively drops the message.
    }
}