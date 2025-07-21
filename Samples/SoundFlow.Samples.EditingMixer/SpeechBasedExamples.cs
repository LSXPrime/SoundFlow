using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Editing;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.EditingMixer;

public static class DialogueFiles
{
    public static readonly string AdamWavPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Adam.wav");
    public static readonly string BellaWavPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bella.wav");

    public static bool CheckFilesExist()
    {
        var adamExists = File.Exists(AdamWavPath);
        var bellaExists = File.Exists(BellaWavPath);

        if (!adamExists) Console.WriteLine($"Warning: Adam.wav not found at {AdamWavPath}");
        if (!bellaExists) Console.WriteLine($"Warning: Bella.wav not found at {BellaWavPath}");

        return adamExists && bellaExists;
    }
}

public static class SpeechBasedExamples
{
    private static readonly AudioEngine AudioEngine = new MiniAudioEngine();
    private static readonly AudioFormat Format = new() { Channels = 1, SampleRate = 24000, Format = SampleFormat.F32 };

    public static void Run()
    {
        Console.WriteLine("SoundFlow Editing Module - Dialogue Examples");
        Console.WriteLine("===========================================");

        if (!DialogueFiles.CheckFilesExist())
        {
            Console.WriteLine("Please ensure Adam.wav and Bella.wav are present to run these examples.");
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
            return;
        }


        var running = true;
        while (running)
        {
            Console.WriteLine("\nChoose a dialogue example to run:");
            Console.WriteLine(" 1. Stitch Full Dialogue (Adam & Bella Alternating)");
            Console.WriteLine(" 2. Replace Bella's Line with a Beep");
            Console.WriteLine(" 3. Remove Adam's Question about Karaoke");
            Console.WriteLine(" 4. Insert a Jingle Before Adam's First Line");
            Console.WriteLine(" 5. Shorten Dialogue by Trimming Ends");
            Console.WriteLine(" 6. Layer Music Under Bella's Intro");
            Console.WriteLine(" 7. Reverse Adam's \"What's the catch?\" Line");
            Console.WriteLine(" 8. Loop Bella's \"This is huge!\" Line");
            Console.WriteLine(" 9. Fade Out Last Line of Dialogue");
            Console.WriteLine("10. Mute Bella's Track Temporarily");
            Console.WriteLine("11. Solo Adam's Track");
            Console.WriteLine("12. Apply Speed Change to Bella's Enthusiasm");
            Console.WriteLine("13. Time Stretch Adam's Line (Longer, Preserving Pitch)");
            Console.WriteLine("14. Time Compress Adam's Line (Shorter, Preserving Pitch)");
            Console.WriteLine("15. Time Stretch by Target Duration (Preserving Pitch)");
            Console.WriteLine("16. Time Stretch then Speed Change");
            Console.WriteLine(" 0. Exit");
            Console.Write("Enter your choice: ");

            if (int.TryParse(Console.ReadLine(), out var choice))
            {
                switch (choice)
                {
                    case 1: RunExample(StitchFullDialogue, "Stitch Full Dialogue"); break;
                    case 2: RunExample(ReplaceBellaLineWithBeep, "Replace Bella Line"); break;
                    case 3: RunExample(RemoveAdamKaraokeQuestion, "Remove Adam's Question"); break;
                    case 4: RunExample(InsertJingleBeforeAdam, "Insert Jingle"); break;
                    case 5: RunExample(ShortenDialogueByTrimming, "Shorten Dialogue"); break;
                    case 6: RunExample(LayerMusicUnderIntro, "Layer Music"); break;
                    case 7: RunExample(ReverseAdamCatchLine, "Reverse Adam's Line"); break;
                    case 8: RunExample(LoopBellaHugeLine, "Loop Bella's Line"); break;
                    case 9: RunExample(FadeOutLastLine, "Fade Out Last Line"); break;
                    case 10: RunExample(MuteBellaTrackTemporarily, "Mute Bella's Track"); break;
                    case 11: RunExample(SoloAdamTrack, "Solo Adam's Track"); break;
                    case 12: RunExample(SpeedChangeBellaEnthusiasm, "Speed Change Bella"); break;
                    case 13: RunExample(TimeStretchAdamLonger, "Time Stretch Adam Longer"); break;
                    case 14: RunExample(TimeCompressAdamShorter, "Time Compress Adam Shorter"); break;
                    case 15: RunExample(TimeStretchAdamByTargetDuration, "Time Stretch by Target Duration"); break;
                    case 16: RunExample(TimeStretchThenSpeedChange, "Time Stretch then Speed Change"); break;
                    case 0: running = false; break;
                    default: Console.WriteLine("Invalid choice. Please try again."); break;
                }
                
                Console.Clear();
            }
            else
            {
                Console.WriteLine("Invalid input. Please enter a number.");
            }
        }

        AudioEngine.Dispose();
        Console.WriteLine("Exited.");
    }

    private static void RunExample(Func<Composition>? exampleFunc, string exampleName)
    {
        Console.WriteLine($"\nRunning: {exampleName}");
        if (!DialogueFiles.CheckFilesExist() && !exampleName.Contains("(Generated)"))
        {
            Console.WriteLine("Dialogue files missing. Skipping example.");
            Console.WriteLine("Press any key to return to the menu...");
            Console.ReadKey();
            Console.Clear();
            return;
        }

        try
        {
            var composition = exampleFunc?.Invoke();
            if (composition != null)
                PlayComposition(composition);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred in {exampleName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        Console.WriteLine($"Finished: {exampleName}");
        Console.WriteLine("Press any key to return to the menu...");
        Console.ReadKey();
        Console.Clear();
    }

    private static void PlayComposition(Composition composition, string message = "Playing composition...")
    {
        Console.WriteLine(message);
        AudioEngine.UpdateDevicesInfo();
        DeviceInfo? deviceInfo = AudioEngine.PlaybackDevices.FirstOrDefault(d => d.IsDefault);
        if (deviceInfo == null)
        {
            Console.WriteLine("No playback device found. Skipping playback.");
            composition.Dispose();
            return;
        }
        
        using var playbackDevice = AudioEngine.InitializePlaybackDevice(deviceInfo.Value, Format);
        playbackDevice.Start();

        using var player = new SoundPlayer(AudioEngine, Format, composition);
        playbackDevice.MasterMixer.AddComponent(player);
        player.Play();

        Console.WriteLine("Press 's' to stop playback early, or wait for it to finish.");
        var compositionDuration = composition.CalculateTotalDuration();
        var startTime = DateTime.Now;

        while (player.State == PlaybackState.Playing)
        {
            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.S)
            {
                player.Stop();
                Console.WriteLine("Playback stopped by user.");
                break;
            }
            var elapsed = DateTime.Now - startTime;
            Console.Write($"\rPlayback time: {elapsed:mm\\:ss\\.fff} / {compositionDuration:mm\\:ss\\.fff}   ");
            Thread.Sleep(50);
            if (elapsed > compositionDuration + TimeSpan.FromSeconds(1)) 
            {
                player.Stop(); // Ensure stop if duration is slightly off
                break;
            }
        }
        Console.WriteLine("\nPlayback finished or stopped.");
        playbackDevice.MasterMixer.RemoveComponent(player);
        playbackDevice.Stop();
        composition.Dispose();
    }
    
    private static Composition StitchFullDialogue()
    {
        var composition = new Composition(Format, "Stitch Full Dialogue");
        var adamProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.AdamWavPath));
        var bellaProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.BellaWavPath));

        var track = new Track("Dialogue");
        var currentTime = TimeSpan.Zero;

        // --- Dialogue Turn 1: Bella (00:00:094 - 00:08:924) ---
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:00:094"), DemoAudio.Ts("00:06:504") - DemoAudio.Ts("00:00:094"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("00:06:504") - DemoAudio.Ts("00:00:094");
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:06:504"), DemoAudio.Ts("00:08:924") - DemoAudio.Ts("00:06:504"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("00:08:924") - DemoAudio.Ts("00:06:504");

        // --- Dialogue Turn 2: Adam (00:00:106 - 00:09:476) ---
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:00:106"), DemoAudio.Ts("00:09:476") - DemoAudio.Ts("00:00:106"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("00:09:476") - DemoAudio.Ts("00:00:106");

        // --- Dialogue Turn 3: Bella (00:09:124 - 00:27:814) ---
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:09:124"), DemoAudio.Ts("00:11:794") - DemoAudio.Ts("00:09:124"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("00:11:794") - DemoAudio.Ts("00:09:124");
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:11:794"), DemoAudio.Ts("00:12:654") - DemoAudio.Ts("00:11:794"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("00:12:654") - DemoAudio.Ts("00:11:794");
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:12:654"), DemoAudio.Ts("00:15:934") - DemoAudio.Ts("00:12:654"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("00:15:934") - DemoAudio.Ts("00:12:654");
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:21:404"), DemoAudio.Ts("00:24:624") - DemoAudio.Ts("00:21:404"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("00:24:624") - DemoAudio.Ts("00:21:404");
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:24:624"), DemoAudio.Ts("00:27:814") - DemoAudio.Ts("00:24:624"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("00:27:814") - DemoAudio.Ts("00:24:624");

        // --- Dialogue Turn 4: Adam (00:09:886 - 00:23:096) ---
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:09:886"), DemoAudio.Ts("00:11:646") - DemoAudio.Ts("00:09:886"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("00:11:646") - DemoAudio.Ts("00:09:886");
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:12:006"), DemoAudio.Ts("00:19:146") - DemoAudio.Ts("00:12:006"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("00:19:146") - DemoAudio.Ts("00:12:006");
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:19:396"), DemoAudio.Ts("00:20:946") - DemoAudio.Ts("00:19:396"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("00:20:946") - DemoAudio.Ts("00:19:396");
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:21:206"), DemoAudio.Ts("00:23:096") - DemoAudio.Ts("00:21:206"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("00:23:096") - DemoAudio.Ts("00:21:206");

        // --- Dialogue Turn 5: Bella (00:28:444 - 00:53:184) ---
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:28:444"), DemoAudio.Ts("00:53:184") - DemoAudio.Ts("00:28:444"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("00:53:184") - DemoAudio.Ts("00:28:444");

        // --- Dialogue Turn 6: Adam (00:23:676 - 00:33:046) ---
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:23:676"), DemoAudio.Ts("00:33:046") - DemoAudio.Ts("00:23:676"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("00:33:046") - DemoAudio.Ts("00:23:676");

        // --- Dialogue Turn 7: Bella (00:53:724 - 01:17:154) ---
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:53:724"), DemoAudio.Ts("01:17:154") - DemoAudio.Ts("00:53:724"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("01:17:154") - DemoAudio.Ts("00:53:724");

        // --- Dialogue Turn 8: Adam (00:33:386 - 00:48:866) ---
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:33:386"), DemoAudio.Ts("00:48:866") - DemoAudio.Ts("00:33:386"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("00:48:866") - DemoAudio.Ts("00:33:386");

        // --- Dialogue Turn 9: Bella (01:17:544 - 01:31:734) ---
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("01:17:544"), DemoAudio.Ts("01:31:734") - DemoAudio.Ts("01:17:544"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("01:31:734") - DemoAudio.Ts("01:17:544");

        // --- Dialogue Turn 10: Adam (00:49:156 - 01:06:916) ---
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:49:156"), DemoAudio.Ts("01:06:916") - DemoAudio.Ts("00:49:156"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("01:06:916") - DemoAudio.Ts("00:49:156");

        // --- Dialogue Turn 11: Bella (01:32:004 - 01:51:764) ---
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("01:32:004"), DemoAudio.Ts("01:51:764") - DemoAudio.Ts("01:32:004"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("01:51:764") - DemoAudio.Ts("01:32:004");

        // --- Dialogue Turn 12: Adam (01:07:376 - 01:11:156) ---
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("01:07:376"), DemoAudio.Ts("01:11:156") - DemoAudio.Ts("01:07:376"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("01:11:156") - DemoAudio.Ts("01:07:376");

        // --- Dialogue Turn 13: Bella (01:52:034 - 02:08:794) ---
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("01:52:034"), DemoAudio.Ts("02:08:794") - DemoAudio.Ts("01:52:034"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("02:08:794") - DemoAudio.Ts("01:52:034");

        // --- Dialogue Turn 14: Adam (01:11:806 - 01:21:096) ---
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("01:11:806"), DemoAudio.Ts("01:21:096") - DemoAudio.Ts("01:11:806"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("01:21:096") - DemoAudio.Ts("01:11:806");

        // --- Dialogue Turn 15: Bella (02:09:124 - 02:11:084) ---
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("02:09:124"), DemoAudio.Ts("02:11:084") - DemoAudio.Ts("02:09:124"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("02:11:084") - DemoAudio.Ts("02:09:124");

        // --- Dialogue Turn 16: Adam (01:21:566 - 01:36:516) ---
        track.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("01:21:566"), DemoAudio.Ts("01:36:516") - DemoAudio.Ts("01:21:566"), currentTime, name: "Adam"));
        currentTime += DemoAudio.Ts("01:36:516") - DemoAudio.Ts("01:21:566");

        // --- Dialogue Turn 17: Bella (02:11:804 - 02:15:374) ---
        track.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("02:11:804"), DemoAudio.Ts("02:15:374") - DemoAudio.Ts("02:11:804"), currentTime, name: "Bella"));
        currentTime += DemoAudio.Ts("02:15:374") - DemoAudio.Ts("02:11:804");
        
        Console.WriteLine($@"Playing stitched dialogue, Total Duration: {composition.CalculateTotalDuration():mm\:ss\.fff}, Calculated Duration: {currentTime:mm\:ss\.fff}");

        composition.AddTrack(track);
        return composition;
    }
    
    private static Composition ReplaceBellaLineWithBeep()
    {
        var composition = new Composition(Format);
        var bellaProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.BellaWavPath));
        var beepProvider = DemoAudio.GenerateShortBeep(TimeSpan.FromSeconds(2.67));

        var track = new Track("Bella's Dialogue");
        var currentTime = TimeSpan.Zero;

        // Bella's first line (before replacement)
        var bellaLine1Start = DemoAudio.Ts("00:00:094");
        var bellaLine1End = DemoAudio.Ts("00:06:504");
        var bellaLine1Duration = bellaLine1End - bellaLine1Start;
        track.AddSegment(new AudioSegment(Format, bellaProvider, bellaLine1Start, bellaLine1Duration, currentTime));
        currentTime += bellaLine1Duration;

        // The line to replace: "It's called SoundFlow." (00:06:504 - 00:08:924)
        var replaceStartOriginal = DemoAudio.Ts("00:06:504");
        var replaceEndOriginal = DemoAudio.Ts("00:08:924");
        var replaceDuration = replaceEndOriginal - replaceStartOriginal;

        var segmentToReplace = new AudioSegment(Format, bellaProvider, replaceStartOriginal, replaceDuration, currentTime, "To Replace");
        track.AddSegment(segmentToReplace);
        currentTime += replaceDuration;

        // Bella's next line
        var bellaLine3Start = DemoAudio.Ts("00:09:124");
        var bellaLine3End = DemoAudio.Ts("00:11:794");
        var bellaLine3Duration = bellaLine3End - bellaLine3Start;
        track.AddSegment(new AudioSegment(Format, bellaProvider, bellaLine3Start, bellaLine3Duration, currentTime, "Next Line"));
            
        composition.AddTrack(track);

        Console.WriteLine($"Replacing Bella's line '{segmentToReplace.Name}' at {segmentToReplace.TimelineStartTime} with a beep.");
        composition.ReplaceSegment(track, segmentToReplace.TimelineStartTime, segmentToReplace.TimelineEndTime, beepProvider, TimeSpan.Zero, replaceDuration);

        return composition;
    }

    private static Composition RemoveAdamKaraokeQuestion()
    {
        var composition = StitchFullDialogue();
        var adamTrack = composition.Tracks.FirstOrDefault(t => t.Segments.Any(s => s.Name.Equals("Adam")));
            
        if (adamTrack == null)
        {
            Console.WriteLine("Adam's track not found in stitched dialogue for removal.");
            return composition;
        }

        // Define the precise source audio timestamps for the segment to be removed.
        // This corresponds to "Dialogue Turn 6: Adam (00:23:676 - 00:33:046)" in StitchFullDialogue.
        var targetSourceStartTime = DemoAudio.Ts("00:23:676");
        var targetSourceDuration = DemoAudio.Ts("00:33:046") - targetSourceStartTime;

        // Find the segment within Adam's track that matches these source properties exactly.
        var segmentToRemove = adamTrack.Segments.FirstOrDefault(s =>
            s.Name.Equals("Adam") &&
            s.SourceStartTime == targetSourceStartTime &&
            s.SourceDuration == targetSourceDuration);

        if (segmentToRemove != null)
        {
            Console.WriteLine($@"Removing segment '{segmentToRemove.Name}' from source {segmentToRemove.SourceStartTime:mm\:ss\.fff} for duration {segmentToRemove.SourceDuration:mm\:ss\.fff} (Adam's karaoke question) and shifting subsequent segments.");
            adamTrack.RemoveSegment(segmentToRemove, shiftSubsequent: true);
        }
        else
        {
            Console.WriteLine($@"Could not find Adam's karaoke question segment to remove. Looked for SourceStartTime: {targetSourceStartTime:mm\:ss\.fff}, SourceDuration: {targetSourceDuration:mm\:ss\.fff}.");
        }
        return composition;
    }

    private static Composition InsertJingleBeforeAdam()
    {
        var composition = StitchFullDialogue();
        var dialogueTrack = composition.Tracks.First();

        var jingleProvider = DemoAudio.GenerateShortBeep(TimeSpan.FromSeconds(1.5)); // 1.5s jingle
        var jingleSegment = new AudioSegment(Format, jingleProvider, TimeSpan.Zero, TimeSpan.FromSeconds(1.5), TimeSpan.Zero, "Jingle", ownsDataProvider: true);

        // Find Adam's first line. In our partial stitch, it starts after Bella's initial lines.
        // Bella's first two segments total: (6.504-0.094) + (8.924-6.504) = 6.41 + 2.42 = 8.83 seconds.
        TimeSpan adamsFirstLineStartTime = DemoAudio.Ts("00:08:830"); // Approximate

        Console.WriteLine($"Inserting jingle before Adam's first line (around {adamsFirstLineStartTime}).");
        dialogueTrack.InsertSegmentAt(jingleSegment, adamsFirstLineStartTime, shiftSubsequent: true);

        return composition;
    }

    private static Composition ShortenDialogueByTrimming()
    {
        var composition = new Composition(Format);
        var bellaProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.BellaWavPath));
        var adamProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.AdamWavPath));

        var track = new Track("Trimmed Dialogue");
        TimeSpan currentTime = TimeSpan.Zero;

        // Bella's "It's called SoundFlow." (trimmed)
        var bellaStart = DemoAudio.Ts("00:06:504");
        var bellaEnd = DemoAudio.Ts("00:08:924");
        var bellaTrimmedSourceStart = bellaStart + TimeSpan.FromMilliseconds(500); // Start a bit later
        var bellaTrimmedSourceDuration = (bellaEnd - bellaStart) - TimeSpan.FromMilliseconds(1000); // Make it shorter
        track.AddSegment(new AudioSegment(Format, bellaProvider, bellaTrimmedSourceStart, bellaTrimmedSourceDuration, currentTime, "Bella Trimmed"));
        currentTime += bellaTrimmedSourceDuration;

        // Adam's "Okay, that’s a significant win." (trimmed)
        var adamStart = DemoAudio.Ts("00:09:886");
        var adamEnd = DemoAudio.Ts("00:11:646");
        var adamTrimmedSourceStart = adamStart + TimeSpan.FromMilliseconds(200);
        var adamTrimmedSourceDuration = (adamEnd - adamStart) - TimeSpan.FromMilliseconds(500);
        track.AddSegment(new AudioSegment(Format, adamProvider, adamTrimmedSourceStart, adamTrimmedSourceDuration, currentTime, "Adam Trimmed"));

        composition.AddTrack(track);
        Console.WriteLine("Playing shortened dialogue by trimming segment ends.");
        return composition;
    }

    private static Composition LayerMusicUnderIntro()
    {
        var composition = new Composition(Format);
        var bellaProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.BellaWavPath));
        var musicProvider = DemoAudio.GenerateMusicLoop(TimeSpan.FromSeconds(10)); // Long enough music

        // Bella's Intro Track
        var bellaTrack = new Track("Bella's Intro");
        var bellaIntroStart = DemoAudio.Ts("00:00:094");
        var bellaIntroEnd = DemoAudio.Ts("00:08:924");
        var bellaIntroDuration = bellaIntroEnd - bellaIntroStart;
        bellaTrack.AddSegment(new AudioSegment(Format, bellaProvider, bellaIntroStart, bellaIntroDuration, TimeSpan.Zero, "Bella Intro"));
        composition.AddTrack(bellaTrack);

        // Music Track
        var musicTrack = new Track("Background Music");
        var musicSettings = new AudioSegmentSettings { Volume = 0.3f }; // Quiet music
        musicTrack.AddSegment(new AudioSegment(Format, musicProvider, TimeSpan.Zero, bellaIntroDuration, TimeSpan.Zero, "BG Music", musicSettings, ownsDataProvider: true));
        composition.AddTrack(musicTrack);

        Console.WriteLine("Playing Bella's intro with background music layered underneath.");
        return composition;
    }

    private static Composition ReverseAdamCatchLine()
    {
        var composition = new Composition(Format);
        var adamProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.AdamWavPath));
        var track = new Track("Adam Reversed");

        // Adam: "What's the catch?" (01:09:206 - 01:10:046)
        var lineStart = DemoAudio.Ts("01:09:206");
        var lineEnd = DemoAudio.Ts("01:10:046");
        var lineDuration = lineEnd - lineStart;

        var settings = new AudioSegmentSettings { IsReversed = true };
        track.AddSegment(new AudioSegment(Format, adamProvider, lineStart, lineDuration, TimeSpan.Zero, "Adam Reversed Catch", settings));
        composition.AddTrack(track);

        Console.WriteLine("Playing Adam's 'What's the catch?' line in reverse.");
        return composition;
    }

    private static Composition LoopBellaHugeLine()
    {
        var composition = new Composition(Format);
        var bellaProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.BellaWavPath));
        var track = new Track("Bella Looped");

        // Bella: "Apm, this is huge!" (01:00:084 - 01:01:744)
        var lineStart = DemoAudio.Ts("01:00:084");
        var lineEnd = DemoAudio.Ts("01:01:744");
        var lineDuration = lineEnd - lineStart;

        var settings = new AudioSegmentSettings { Loop = new LoopSettings(repetitions: 3) }; // Play 4 times total
        track.AddSegment(new AudioSegment(Format, bellaProvider, lineStart, lineDuration, TimeSpan.Zero, "Bella Huge Loop", settings));
        composition.AddTrack(track);

        Console.WriteLine("Looping Bella's 'Apm, This is huge!' line 4 times.");
        return composition;
    }

    private static Composition FadeOutLastLine()
    {
        var composition = StitchFullDialogue();
        var dialogueTrack = composition.Tracks.First();

        if (dialogueTrack.Segments.Count != 0)
        {
            var lastSegment = dialogueTrack.Segments.Last();
            Console.WriteLine($"Adding 2-second S-Curve fade out to segment: '{lastSegment.Name}' starting at {lastSegment.TimelineStartTime}");
            lastSegment.Settings.FadeOutDuration = TimeSpan.FromSeconds(2);
            lastSegment.Settings.FadeOutCurve = FadeCurveType.SCurve;
        }
        else
        {
            Console.WriteLine("No segments in dialogue to fade out.");
        }
        return composition;
    }

    private static Composition MuteBellaTrackTemporarily()
    {
        var composition = new Composition(Format);
        var adamProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.AdamWavPath));
        var bellaProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.BellaWavPath));
            
        var adamTrack = new Track("Adam");
        adamTrack.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:00:106"), DemoAudio.Ts("00:09:476") - DemoAudio.Ts("00:00:106"), TimeSpan.Zero));
        composition.AddTrack(adamTrack);

        var bellaTrack = new Track("Bella (Muted)") { Settings = { IsMuted = true } };
        bellaTrack.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:00:094"), DemoAudio.Ts("00:08:924") - DemoAudio.Ts("00:00:094"), TimeSpan.FromSeconds(0.5))); // Slightly offset
        composition.AddTrack(bellaTrack);

        Console.WriteLine("Bella's track is muted. Only Adam should be heard.");
        return composition;
    }

    private static Composition SoloAdamTrack()
    {
        var composition = new Composition(Format);
        var adamProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.AdamWavPath));
        var bellaProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.BellaWavPath));
            
        var adamTrack = new Track("Adam (Soloed)") { Settings = { IsSoloed = true } };
        adamTrack.AddSegment(new AudioSegment(Format, adamProvider, DemoAudio.Ts("00:00:106"), DemoAudio.Ts("00:09:476") - DemoAudio.Ts("00:00:106"), TimeSpan.Zero));
        composition.AddTrack(adamTrack);

        var bellaTrack = new Track("Bella");
        bellaTrack.AddSegment(new AudioSegment(Format, bellaProvider, DemoAudio.Ts("00:00:094"), DemoAudio.Ts("00:08:924") - DemoAudio.Ts("00:00:094"), TimeSpan.FromSeconds(0.5)));
        composition.AddTrack(bellaTrack);
            
        Console.WriteLine("Adam's track is soloed. Only Adam should be heard.");
        return composition;
    }

    private static Composition SpeedChangeBellaEnthusiasm()
    {
        var composition = new Composition(Format);
        var bellaProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.BellaWavPath));
        var track = new Track("Bella Speed Change");

        // Bella: "The possibilities are endless." (01:50:094 - 01:51:764)
        var lineStart = DemoAudio.Ts("01:50:094");
        var lineEnd = DemoAudio.Ts("01:51:764");
        var lineDuration = lineEnd - lineStart;
            
        var settings = new AudioSegmentSettings { SpeedFactor = 1.3f }; // 30% faster
        track.AddSegment(new AudioSegment(Format, bellaProvider, lineStart, lineDuration, TimeSpan.Zero, "Bella Faster", settings));
        composition.AddTrack(track);

        Console.WriteLine("Playing Bella's 'The possibilities are endless' 30% faster (pitch will also increase).");
        return composition;
    }

    private static Composition TimeStretchAdamLonger()
    {
        var composition = new Composition(Format, "TimeStretchAdamLonger");
        var adamProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.AdamWavPath));
        var track = new Track("Adam Stretched (Longer)");

        // Original duration: 13 seconds
        var lineSourceStart = DemoAudio.Ts("00:36:206");
        var lineSourceEnd = DemoAudio.Ts("00:49:046");
        var lineSourceDuration = lineSourceEnd - lineSourceStart;

        var settings = new AudioSegmentSettings
        {
            TimeStretchFactor = 1.5f // Make it 50% longer, preserving pitch
        };
        var segment = new AudioSegment(Format, adamProvider, lineSourceStart, lineSourceDuration, TimeSpan.Zero, "Adam Stretched 1.5x", settings, ownsDataProvider:true);
        track.AddSegment(segment);
        composition.AddTrack(track);

        Console.WriteLine($"Playing Adam's line stretched to be 50% longer (pitch preserved). Original duration: {lineSourceDuration}, New Effective: {segment.EffectiveDurationOnTimeline}");
        return composition;
    }

    private static Composition TimeCompressAdamShorter()
    {
        var composition = new Composition(Format, "TimeCompressAdamShorter");
        var adamProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.AdamWavPath));
        var track = new Track("Adam Compressed (Shorter)");

        // Adam: "This sounds almost too good to be true." (01:07:376 - 01:09:206 in source)
        // Original duration: 1.83 seconds
        var lineSourceStart = DemoAudio.Ts("01:07:376");
        var lineSourceEnd = DemoAudio.Ts("01:09:206");
        var lineSourceDuration = lineSourceEnd - lineSourceStart;

        var settings = new AudioSegmentSettings
        {
            TimeStretchFactor = 0.7f // Make it 30% shorter, preserving pitch
        };
        var segment = new AudioSegment(Format, adamProvider, lineSourceStart, lineSourceDuration, TimeSpan.Zero, "Adam Compressed 0.7x", settings, ownsDataProvider:true);
        track.AddSegment(segment);
        composition.AddTrack(track);

        Console.WriteLine($"Playing Adam's 'This sounds almost too good...' compressed to be 30% shorter (pitch preserved). Original duration: {lineSourceDuration}, New Effective: {segment.EffectiveDurationOnTimeline}");
        return composition;
    }

    private static Composition TimeStretchAdamByTargetDuration()
    {
        var composition = new Composition(Format, "TimeStretchTargetDuration");
        var adamProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.AdamWavPath));
        var track = new Track("Adam Stretched by Target Duration");

        // Adam: "Okay, Bella, you've definitely got my attention." (01:11:806 - 01:15:026 in source)
        // Original duration: approx 3.22 seconds
        var lineSourceStart = DemoAudio.Ts("01:11:806");
        var lineSourceEnd = DemoAudio.Ts("01:15:026");
        var lineSourceDuration = lineSourceEnd - lineSourceStart;

        var targetDuration = TimeSpan.FromSeconds(5.0); // Stretch to 5 seconds

        var settings = new AudioSegmentSettings
        {
            TargetStretchDuration = targetDuration // Stretch to this duration, preserving pitch
        };
        var segment = new AudioSegment(Format, adamProvider, lineSourceStart, lineSourceDuration, TimeSpan.Zero, "Adam Stretched to 5s", settings, ownsDataProvider:true);
        track.AddSegment(segment);
        composition.AddTrack(track);

        Console.WriteLine($"Playing Adam's line stretched to target duration of {targetDuration} (pitch preserved). Original duration: {lineSourceDuration}, New Effective: {segment.EffectiveDurationOnTimeline}, Stretch Factor: {segment.Settings.TimeStretchFactor:F2}x");
        return composition;
    }

    private static Composition TimeStretchThenSpeedChange()
    {
        var composition = new Composition(Format, "StretchThenSpeed");
        var adamProvider = new StreamDataProvider(AudioEngine, Format, File.OpenRead(DialogueFiles.AdamWavPath));
        var track = new Track("Adam Stretched then Sped Up");

        // Original duration: approx 6.39 seconds
        var lineSourceStart = DemoAudio.Ts("01:30:126");
        var lineSourceEnd = DemoAudio.Ts("01:36:516");
        var lineSourceDuration = lineSourceEnd - lineSourceStart;

        var settings = new AudioSegmentSettings
        {
            TimeStretchFactor = 0.8f, // Compress by 20% (pitch preserved) -> new content duration ~5.11s
            SpeedFactor = 1.2f        // Then speed up by 20% (pitch will increase from the stretched version)
                                      // -> final timeline duration ~5.11s / 1.2 = ~4.26s
        };
        var segment = new AudioSegment(Format, adamProvider, lineSourceStart, lineSourceDuration, TimeSpan.Zero, "Adam Stretch 0.8x, Speed 1.2x", settings, ownsDataProvider:true);
        track.AddSegment(segment);
        composition.AddTrack(track);

        Console.WriteLine($"Playing Adam's line: TimeStretchFactor={settings.TimeStretchFactor:F2}x (pitch preserved), then SpeedFactor={settings.SpeedFactor:F2}x (pitch changes).");
        Console.WriteLine($"Original duration: {lineSourceDuration}. Stretched Content Duration: {segment.StretchedSourceDuration}. Final Timeline Duration: {segment.EffectiveDurationOnTimeline}");
        return composition;
    }
}