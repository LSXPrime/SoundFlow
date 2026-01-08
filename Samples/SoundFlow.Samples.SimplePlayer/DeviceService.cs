using SoundFlow.Abstracts;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace SoundFlow.Samples.SimplePlayer;

/// <summary>
/// Provides helper methods for audio device selection and management.
/// </summary>
public static class DeviceService
{
    /// <summary>
    /// Prompts the user to select a single device from a list of available devices.
    /// </summary>
    /// <param name="engine">The audio engine instance to query for devices.</param>
    /// <param name="type">The type of device to select (Playback or Capture).</param>
    /// <returns>The selected device information, or null if no device is found or selected.</returns>
    public static DeviceInfo? SelectDevice(AudioEngine engine, DeviceType type)
    {
        engine.UpdateAudioDevicesInfo();
        var devices = type == DeviceType.Playback ? engine.PlaybackDevices : engine.CaptureDevices;

        if (devices.Length == 0)
        {
            Console.WriteLine($"No {type.ToString().ToLower()} devices found.");
            return null;
        }

        Console.WriteLine($"\nPlease select a {type.ToString().ToLower()} device:");
        for (var i = 0; i < devices.Length; i++)
        {
            Console.WriteLine($"  {i}: {devices[i].Name} {(devices[i].IsDefault ? "(Default)" : "")}");
        }

        while (true)
        {
            Console.Write("Enter device index: ");
            if (int.TryParse(Console.ReadLine(), out var index) && index >= 0 && index < devices.Length)
            {
                return devices[index];
            }
            Console.WriteLine("Invalid index. Please try again.");
        }
    }
}