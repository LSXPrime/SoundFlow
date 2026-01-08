using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace SoundFlow.Samples.Recording;

/// <summary>
/// Helper service to list and select audio capture devices.
/// </summary>
public static class DeviceService
{
    /// <summary>
    /// Lists available capture devices and prompts the user to select one.
    /// </summary>
    /// <param name="engine">The audio engine instance.</param>
    /// <returns>The selected device info, or null if cancelled/invalid.</returns>
    public static DeviceInfo? SelectInputDevice(AudioEngine engine)
    {
        var devices = engine.CaptureDevices;

        if (devices.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No audio capture devices found.");
            Console.ResetColor();
            return null;
        }

        Console.WriteLine("\nAvailable Input Devices:");
        for (var i = 0; i < devices.Length; i++)
        {
            var dev = devices[i];
            var defaultMarker = dev.IsDefault ? " [Default]" : "";
            Console.WriteLine($"  {i + 1}. {dev.Name}{defaultMarker}");
        }

        while (true)
        {
            Console.Write("\nSelect device number (or '0' for System Default): ");
            var input = Console.ReadLine()?.Trim();

            if (input == "0")
            {
                return devices.FirstOrDefault(d => d.IsDefault);
            }

            if (int.TryParse(input, out var index) && index > 0 && index <= devices.Length)
            {
                return devices[index - 1];
            }

            Console.WriteLine("Invalid selection. Please try again.");
        }
    }
}