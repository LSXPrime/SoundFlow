using SoundFlow.Components;
using SoundFlow.Enums;

namespace SoundFlow.Samples.SimplePlayer;

/// <summary>
/// Provides reusable UI components for the console application.
/// </summary>
public static class UserInterfaceService
{
    /// <summary>
    /// Displays an interactive set of playback controls for an ISoundPlayer instance.
    /// </summary>
    /// <param name="player">The player to control.</param>
    public static void DisplayPlaybackControls(SoundPlayer player)
    {
        Console.WriteLine("\n--- Playback Controls ---");
        Console.WriteLine("'P': Play/Pause | 'S': Seek | 'V': Volume | '+/-': Speed | 'T': Switch Time Stretch Quality | 'R': Reset Speed | Any other: Stop");
        
        using var timer = new System.Timers.Timer(500);
        timer.AutoReset = true;
        timer.Elapsed += (_, _) =>
        {
            if (player.State != PlaybackState.Stopped)
            {
                // Use Console.SetCursorPosition to prevent flickering/scrolling on some terminals
                var originalLeft = Console.CursorLeft;
                var originalTop = Console.CursorTop;
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write(new string(' ', Console.WindowWidth - 1)); // Clear the line
                Console.SetCursorPosition(0, Console.CursorTop);
                Console.Write($"\rTime: {TimeSpan.FromSeconds(player.Time):mm\\:ss\\.ff} / {TimeSpan.FromSeconds(player.Duration):mm\\:ss\\.ff} | Speed: {player.PlaybackSpeed:F1}x | Vol: {player.Volume:F1}  ");
                if (originalLeft < Console.WindowWidth)
                {
                    Console.SetCursorPosition(originalLeft, originalTop);
                }
            }
        };
        timer.Start();
        
        var defaultQuality = WsolaPerformancePreset.Balanced;
        var currentQuality = defaultQuality;
        
        while (player.State is PlaybackState.Playing or PlaybackState.Paused)
        {
            var keyInfo = Console.ReadKey(true);
            switch (keyInfo.Key)
            {
                case ConsoleKey.P:
                    if (player.State == PlaybackState.Playing) player.Pause();
                    else player.Play();
                    break;
                case ConsoleKey.S:
                    Console.Write("\nEnter seek time in seconds (e.g., 5.0): ");
                    if (double.TryParse(Console.ReadLine(), out var seekTime)) player.Seek(TimeSpan.FromSeconds(seekTime));
                    else Console.WriteLine("Invalid seek time.");
                    break;
                case ConsoleKey.OemPlus or ConsoleKey.Add:
                    player.PlaybackSpeed = Math.Min(player.PlaybackSpeed + 0.1f, 4.0f);
                    break;
                case ConsoleKey.OemMinus or ConsoleKey.Subtract:
                    player.PlaybackSpeed = Math.Max(0.1f, player.PlaybackSpeed - 0.1f);
                    break;
                case ConsoleKey.T:
                    currentQuality = (WsolaPerformancePreset)(((int)currentQuality + 1) % 4);
                    player.SetTimeStretchQuality(currentQuality);
                    Console.WriteLine($"\nTime Stretch Quality set to: {currentQuality}");
                    break;
                case ConsoleKey.R:
                    player.PlaybackSpeed = 1.0f;
                    break;
                case ConsoleKey.V:
                    Console.Write("\nEnter volume (0.0 to 2.0): ");
                    if (float.TryParse(Console.ReadLine(), out var volume))
                        player.Volume = Math.Clamp(volume, 0.0f, 2.0f);
                    else
                        Console.WriteLine("Invalid volume.");
                    break;
                default:
                    player.Stop();
                    break;
            }
        }

        timer.Stop();
        // Clear the status line after stopping
        Console.Write(new string(' ', Console.WindowWidth - 1) + "\r");
        Console.WriteLine("\nPlayback stopped.");
    }
}