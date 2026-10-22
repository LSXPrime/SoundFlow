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
    /// <param name="device">The playback device to preserve.</param>
    /// <returns>A read-only collection of sound components.</returns>
    public static IReadOnlyCollection<SoundComponent> PreservePlaybackState(AudioPlaybackDevice device)
    {
        // Return a copy of the list of components from the master mixer.
        return device.MasterMixer.Components;
    }

    /// <summary>
    /// Restores the state to a new playback device by re-adding the preserved components.
    /// </summary>
    /// <param name="device">The new playback device.</param>
    /// <param name="components">The preserved sound components.</param>
    public static void RestorePlaybackState(AudioPlaybackDevice device, IReadOnlyCollection<SoundComponent> components)
    {
        foreach (var component in components)
        {
            device.MasterMixer.AddComponent(component);
        }
    }

    /// <summary>
    /// Preserves the state of any audio device by extracting its event subscribers.
    /// </summary>
    /// <param name="device">The device to preserve.</param>
    /// <returns>An array of delegates subscribed to the device's processing event.</returns>
    public static Delegate[] PreserveEventSubscribers(AudioDevice device)
    {
        return device.GetEventSubscribers();
    }

    /// <summary>
    /// Restores the state to a new audio device by re-adding the preserved event subscribers.
    /// </summary>
    /// <param name="device">The new device.</param>
    /// <param name="subscribers">The preserved event subscribers.</param>
    public static void RestoreEventSubscribers(AudioDevice device, Delegate[] subscribers)
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