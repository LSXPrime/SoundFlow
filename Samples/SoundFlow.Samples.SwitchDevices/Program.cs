using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;
using System.Runtime.InteropServices;

namespace SoundFlow.Samples.SwitchDevices;

/// <summary>
/// Example program demonstrating how to switch the output device during live playback, capture, or full-duplex operation.
/// </summary>
internal static class Program
{
    private static readonly AudioEngine Engine = new MiniAudioEngine();
    private static readonly AudioFormat Format = AudioFormat.DvdHq;

    private static void Main()
    {
        try
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("\nSoundFlow Device Switching Menu:");
                Console.WriteLine("1. Playback Device Switch");
                Console.WriteLine("2. Capture Device Switch");
                Console.WriteLine("3. Full-Duplex Device Switch");
                Console.WriteLine("4. Loopback Device Switch");
                Console.WriteLine("Press any other key to exit.");

                var choice = Console.ReadKey(true).KeyChar;
                Console.WriteLine();

                switch (choice)
                {
                    case '1':
                        PlaybackSwitchExample();
                        break;
                    case '2':
                        CaptureSwitchExample();
                        break;
                    case '3':
                        DuplexSwitchExample();
                        break;
                    case '4':
                        LoopbackSwitchExample();
                        break;
                    default:
                        Console.WriteLine("Exiting.");
                        return;
                }

                Console.WriteLine("\nOperation complete. Press any key to return to the menu.");
                Console.ReadKey();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nAn unexpected error occurred: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
        finally
        {
            // Dispose the single engine instance on application exit.
            Engine.Dispose();
        }
    }


    #region Device Switching Methods

    private static void PlaybackSwitchExample()
    {
        // 1. Initialize the default playback device
        Console.WriteLine("Initializing default playback device...");
        var playbackDevice =
            Engine.InitializePlaybackDevice(Engine.PlaybackDevices.FirstOrDefault(x => x.IsDefault), Format);

        // 2. Create an oscillator and add it to the device's mixer
        var oscillator = new Oscillator(Engine, Format)
        {
            Frequency = 440f, // A4 note
            Volume = 0.5f
        };
        playbackDevice.MasterMixer.AddComponent(oscillator);

        // 3. Start playback
        playbackDevice.Start();
        Console.WriteLine($"Playing tone on: {playbackDevice.Info?.Name}");

        // 4. Main loop to allow switching
        while (true)
        {
            Console.WriteLine("\nPress 's' to switch device, or 'q' to quit.");
            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.Q) break;

            if (key == ConsoleKey.S)
            {
                var newDeviceInfo = SelectDevice(DeviceType.Playback);
                if (newDeviceInfo.HasValue)
                {
                    Console.WriteLine($"Switching playback to: {newDeviceInfo.Value.Name}...");

                    // The magic happens here!
                    // The old device is disposed, and a new one is returned.
                    // The oscillator is automatically moved to the new device's mixer.
                    playbackDevice = Engine.SwitchDevice(playbackDevice, newDeviceInfo.Value);

                    Console.WriteLine($"Successfully switched. Now playing on: {playbackDevice.Info?.Name}");
                }
            }
        }

        // 5. Clean up
        playbackDevice.Dispose();
        Console.WriteLine("Playback stopped.");
    }

    private static void CaptureSwitchExample()
    {
        // 1. Initialize the default capture device
        Console.WriteLine("Initializing default capture device...");
        var captureDevice =
            Engine.InitializeCaptureDevice(Engine.CaptureDevices.FirstOrDefault(x => x.IsDefault), Format);

        // 2. Set up a recorder to record to a file
        using var stream = new FileStream("recorded.wav", FileMode.Create);
        var recorder = new Recorder(captureDevice, stream);

        // 3. Start capture and recording
        captureDevice.Start();
        recorder.StartRecording();
        Console.WriteLine($"Recording from: {captureDevice.Info?.Name}");
        Console.WriteLine("You should see dots appearing as audio is captured.");

        // 4. Main loop
        while (true)
        {
            Console.WriteLine("\nPress 's' to switch device, or 'q' to quit.");
            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.Q) break;

            if (key == ConsoleKey.S)
            {
                var newDeviceInfo = SelectDevice(DeviceType.Capture);
                if (newDeviceInfo.HasValue)
                {
                    Console.WriteLine($"\nSwitching capture to: {newDeviceInfo.Value.Name}...");

                    // Switch the capture device. The recorder's event subscription is transferred.
                    captureDevice = Engine.SwitchDevice(captureDevice, newDeviceInfo.Value);

                    Console.WriteLine($"Successfully switched. Now recording from: {captureDevice.Info?.Name}");
                }
            }
        }

        // 5. Clean up
        captureDevice.Dispose(); // Disposing the device will also stop it
        recorder.Dispose();
        Console.WriteLine("\nRecording stopped.");
    }

    private static void DuplexSwitchExample()
    {
        // 1. Initialize the duplex device with default devices
        Console.WriteLine("Initializing default full-duplex device...");
        var duplexDevice = Engine.InitializeFullDuplexDevice(Engine.PlaybackDevices.FirstOrDefault(x => x.IsDefault),
            Engine.CaptureDevices.FirstOrDefault(x => x.IsDefault), Format);

        // 2. Set up a passthrough from capture to playback
        var micProvider = new MicrophoneDataProvider(duplexDevice.CaptureDevice);
        var player = new SoundPlayer(Engine, Format, micProvider);
        duplexDevice.MasterMixer.AddComponent(player);

        // 3. Start everything
        duplexDevice.Start();
        micProvider.StartCapture();
        player.Play();
        Console.WriteLine(
            $"Passthrough active. Input: {duplexDevice.CaptureDevice.Info?.Name}, Output: {duplexDevice.PlaybackDevice.Info?.Name}");

        // 4. Main loop
        while (true)
        {
            Console.WriteLine("\nPress 'i' to switch input, 'o' to switch output, or 'q' to quit.");
            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.Q) break;

            if (key == ConsoleKey.I) // Switch Input
            {
                var newCaptureInfo = SelectDevice(DeviceType.Capture);
                if (newCaptureInfo.HasValue)
                {
                    Console.WriteLine($"\nSwitching input to: {newCaptureInfo.Value.Name}...");
                    // Pass `null` for the playback device to keep it the same
                    duplexDevice = Engine.SwitchDevice(duplexDevice, null, newCaptureInfo.Value);
                    Console.WriteLine(
                        $"Successfully switched. Input: {duplexDevice.CaptureDevice.Info?.Name}, Output: {duplexDevice.PlaybackDevice.Info?.Name}");
                }
            }
            else if (key == ConsoleKey.O) // Switch Output
            {
                var newPlaybackInfo = SelectDevice(DeviceType.Playback);
                if (newPlaybackInfo.HasValue)
                {
                    Console.WriteLine($"\nSwitching output to: {newPlaybackInfo.Value.Name}...");
                    // Pass `null` for the capture device to keep it the same
                    duplexDevice = Engine.SwitchDevice(duplexDevice, newPlaybackInfo.Value, null);
                    Console.WriteLine(
                        $"Successfully switched. Input: {duplexDevice.CaptureDevice.Info?.Name}, Output: {duplexDevice.PlaybackDevice.Info?.Name}");
                }
            }
        }

        // 5. Clean up
        duplexDevice.Dispose();
        player.Dispose();
        micProvider.Dispose();
        Console.WriteLine("\nPassthrough stopped.");
    }

    private static void LoopbackSwitchExample()
    {
        // 1. Initialize the loopback device
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("Loopback recording is only supported on Windows in this example.");
            return;
        }

        Console.WriteLine("Initializing default loopback device...");
        var loopbackDevice = Engine.InitializeLoopbackDevice(Format);

        // 2. Set up a recorder to loopback to a file
        using var stream = new FileStream("recorded_loopback.wav", FileMode.Create);
        var recorder = new Recorder(loopbackDevice, stream);

        // 3. Start everything
        loopbackDevice.Start();
        recorder.StartRecording();
        Console.WriteLine($"Recording system audio from: {loopbackDevice.Info?.Name}");
        Console.WriteLine("Play some audio on your system.");

        // 4. Main loop
        while (true)
        {
            Console.WriteLine("\nPress 's' to switch loopback source device, or 'q' to quit.");
            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.Q) break;

            if (key == ConsoleKey.S)
            {
                // NOTE: To switch a loopback source, we present the user with a list of playback devices.
                // The selected playback device will become the new source for loopback capture.
                var newDeviceInfo = SelectDevice(DeviceType.Playback);
                if (newDeviceInfo.HasValue)
                {
                    Console.WriteLine($"\nSwitching loopback source to: {newDeviceInfo.Value.Name}...");

                    // We are still switching a "CaptureDevice", but we provide it with
                    // the info of the new playback device to capture from.
                    // NOTE: It's necessary to provide a config that have `Capture.IsLoopback` set to `true`, which happens by default in `InitializeLoopbackDevice` only, or manually.
                    loopbackDevice = Engine.SwitchDevice(loopbackDevice, newDeviceInfo.Value, loopbackDevice.Config);

                    Console.WriteLine($"Successfully switched. Now capturing from: {loopbackDevice.Info?.Name}");
                }
            }
        }

        // 5. Clean up
        loopbackDevice.Dispose();
        recorder.Dispose();
        Console.WriteLine("\nLoopback recording stopped, recorded to 'recorded_loopback.wav'");
    }

    #endregion

    #region Helper Methods

    private static DeviceInfo? SelectDevice(DeviceType type)
    {
        Engine.UpdateDevicesInfo();
        var devices = type == DeviceType.Playback ? Engine.PlaybackDevices : Engine.CaptureDevices;

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
                return devices[index];

            Console.WriteLine("Invalid index. Please try again.");
        }
    }

    #endregion
}