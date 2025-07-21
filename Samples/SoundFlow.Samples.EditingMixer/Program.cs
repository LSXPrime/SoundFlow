using SoundFlow.Providers;

namespace SoundFlow.Samples.EditingMixer;

public static class DemoAudio
{
    private const int BaseSampleRate = 44100;
    private const int BaseChannels = 2;

    private static RawDataProvider GenerateTone(TimeSpan duration, float frequency, float amplitude = 0.5f)
    {
        var totalSamples = (int)(duration.TotalSeconds * BaseSampleRate * BaseChannels);
        var samples = new float[totalSamples];
        float phase = 0;
        var phaseIncrement = (2 * MathF.PI * frequency) / BaseSampleRate;

        for (var i = 0; i < totalSamples; i += BaseChannels)
        {
            var value = MathF.Sin(phase) * amplitude;
            for (var ch = 0; ch < BaseChannels; ch++)
            {
                samples[i + ch] = value;
            }
            phase += phaseIncrement;
            if (phase >= 2 * MathF.PI) phase -= 2 * MathF.PI;
        }
        return new RawDataProvider(samples);
    }

    public static RawDataProvider GenerateShortBeep(TimeSpan duration = default)
    {
        if (duration == TimeSpan.Zero) duration = TimeSpan.FromMilliseconds(500);
        return GenerateTone(duration, 880); // A5 tone
    }

    public static RawDataProvider GenerateLongTone(TimeSpan duration = default)
    {
        if (duration == TimeSpan.Zero) duration = TimeSpan.FromSeconds(6);
        return GenerateTone(duration, 440); // A4 tone
    }

    public static RawDataProvider GenerateSpeechFragment(TimeSpan duration = default)
    {
        if (duration == TimeSpan.Zero) duration = TimeSpan.FromSeconds(5);
        return GenerateTone(duration, 220, 0.4f); // Simulating speech with A3
    }

    public static RawDataProvider GenerateMusicLoop(TimeSpan duration = default)
    {
        if (duration == TimeSpan.Zero) duration = TimeSpan.FromSeconds(2);
        return GenerateTone(duration, 660, 0.6f); // E5 tone
    }

    public static RawDataProvider GenerateFxSound(TimeSpan duration = default)
    {
        if (duration == TimeSpan.Zero) duration = TimeSpan.FromSeconds(1);
        return GenerateTone(duration, 1320, 0.7f); // E6 tone
    }

    public static TimeSpan Ts(string timestamp)
    {
        var parts = timestamp.Split([':', '-'], StringSplitOptions.RemoveEmptyEntries);
        return new TimeSpan(0, 0, int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }
}


public static class Program
{
    private static void Main()
    {
        Console.WriteLine("SoundFlow Editing Module Examples");
        Console.WriteLine("=================================");

        var running = true;
        while (running)
        {
            Console.WriteLine("\nChoose an examples set to run:");
            Console.WriteLine(" 1. Speech Dialogue Between Adam & Bella");
            Console.WriteLine(" 2. Basic Tone Based Composition");
            Console.WriteLine(" 3. Project Persistence (Save & Load)");
            Console.WriteLine(" 0. Exit");
            Console.Write("Enter your choice: ");

            if (int.TryParse(Console.ReadLine(), out var choice))
            {
                switch (choice)
                {
                    case 1: SpeechBasedExamples.Run(); break;
                    case 2: ToneBasedExamples.Run(); break;
                    case 3: PersistenceExamples.Run(); break;
                    case 0: running = false; break;
                    default: Console.WriteLine("Invalid choice. Please try again."); break;
                }
            }
            else
            {
                Console.WriteLine("Invalid input. Please enter a number.");
            }
        }

        Console.WriteLine("Exited.");
    }
}