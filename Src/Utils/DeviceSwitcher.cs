using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;

namespace SoundFlow.Utils;

/// <summary>
/// Internal helper class to manage the state transfer when switching audio devices.
/// </summary>
internal static class DeviceSwitcher
{
    /// <summary>
    /// Preserves the state of a playback device by extracting its sound components.
    /// </summary>
    public static IReadOnlyCollection<SoundComponent> PreservePlaybackState(AudioPlaybackDevice device)
    {
        // Return a copy of the list of components from the master mixer.
        return device.MasterMixer.Components;
    }

    /// <summary>
    /// Restores the state to a new playback device by re-adding the preserved components.
    /// </summary>
    public static void RestorePlaybackState(AudioPlaybackDevice device, IReadOnlyCollection<SoundComponent> components)
    {
        foreach (var component in components)
        {
            device.MasterMixer.AddComponent(component);
        }
    }

    /// <summary>
    /// Preserves the state of a capture device by extracting its event subscribers.
    /// </summary>
    public static Delegate[] PreserveCaptureState(AudioCaptureDevice device)
    {
        return device.GetEventSubscribers();
    }

    /// <summary>
    /// Restores the state to a new capture device by re-adding the preserved event subscribers.
    /// </summary>
    public static void RestoreCaptureState(AudioCaptureDevice device, Delegate[] subscribers)
    {
        foreach (var subscriber in subscribers)
        {
            if (subscriber is AudioProcessCallback callback)
            {
                device.OnAudioProcessed += callback;
            }
        }
    }
}