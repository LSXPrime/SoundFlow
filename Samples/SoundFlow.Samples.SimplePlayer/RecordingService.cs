using SoundFlow.Abstracts;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Structs;

namespace SoundFlow.Samples.SimplePlayer;

/// <summary>
/// Encapsulates all logic related to audio recording.
/// </summary>
public static class RecordingService
{
    /// <summary>
    /// Manages a complete recording session, including device selection, recording controls,
    /// and an option to play back the recorded file.
    /// </summary>
    public static void RecordAndPlayback(AudioEngine engine, AudioFormat format, DeviceConfig deviceConfig, string outputFilePath)
    {
        var captureDeviceInfo = DeviceService.SelectDevice(engine, DeviceType.Capture);
        if (!captureDeviceInfo.HasValue) return;

        using var captureDevice = engine.InitializeCaptureDevice(captureDeviceInfo.Value, format, deviceConfig);
        captureDevice.Start();
        
        // The stream must be disposed manually after the recorder is done with it.
        var stream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        try
        {
            using var recorder = new Recorder(captureDevice, stream);
            Console.WriteLine("Recording started. Press 's' to stop, 'p' to pause/resume.");
            recorder.StartRecording();

            while (recorder.State != PlaybackState.Stopped)
            {
                var key = Console.ReadKey(true).Key;
                switch (key)
                {
                    case ConsoleKey.S:
                        recorder.StopRecording();
                        break;
                    case ConsoleKey.P:
                        if (recorder.State == PlaybackState.Paused)
                        {
                            recorder.ResumeRecording();
                            Console.WriteLine("Recording resumed.");
                        }
                        else
                        {
                            recorder.PauseRecording();
                            Console.WriteLine("Recording paused.");
                        }
                        break;
                }
            }
        }
        finally
        {
            stream.Dispose();
            captureDevice.Stop();
        }

        Console.WriteLine($"\nRecording finished. File saved to: {outputFilePath}");
        Console.WriteLine("Press 'p' to play back or any other key to skip.");
        if (Console.ReadKey(true).Key != ConsoleKey.P) return;
        
        PlaybackService.PlayFile(engine, format, deviceConfig, outputFilePath);
    }
}