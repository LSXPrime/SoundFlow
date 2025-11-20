using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Components;
using SoundFlow.Editing;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SoundFlow.Samples.EditingMixer;

public static class ToneBasedExamples
{
    private static readonly AudioEngine AudioEngine = new MiniAudioEngine();
    private static readonly AudioFormat Format = AudioFormat.DvdHq;

    internal static void Run()
    {
        Console.WriteLine("SoundFlow Editing Module Examples");
        Console.WriteLine("=================================");
        Console.WriteLine("Using internally generated audio sounds.");
        
        var running = true;
        while (running)
        {
            Console.WriteLine("\nChoose an example to run:");
            Console.WriteLine(" 1. Basic Composition Playback");
            Console.WriteLine(" 2. Replace Segment");
            Console.WriteLine(" 3. Remove Segment (and shift)");
            Console.WriteLine(" 4. Silence Segment (by adding silent segment)");
            Console.WriteLine(" 5. Insert Segment");
            Console.WriteLine(" 6. Trim/Crop Segment");
            Console.WriteLine(" 7. Duplicate/Clone Segment");
            Console.WriteLine(" 8. Reverse Segment");
            Console.WriteLine(" 9. Loop Segment (Repetitions)");
            Console.WriteLine("10. Loop Segment (Target Duration)");
            Console.WriteLine("11. Multi-Track Composition & Overlay");
            Console.WriteLine("12. Volume Control (Segment & Track)");
            Console.WriteLine("13. Pan Control (Segment & Track)");
            Console.WriteLine("14. Fade In/Out Segment");
            Console.WriteLine("15. Mute Track");
            Console.WriteLine("16. Solo Track");
            Console.WriteLine("17. Offset Segment on Timeline");
            Console.WriteLine("18. Speed Change");
            Console.WriteLine("19. Query Composition Duration");
            Console.WriteLine("20. Render Composition to float[] (and play)");
            Console.WriteLine(" 0. Exit");
            Console.Write("Enter your choice: ");
            
            if (int.TryParse(Console.ReadLine(), out var choice))
            {
                switch (choice)
                {
                    case 1: RunExample(BasicCompositionPlayback, "Basic Playback"); break;
                    case 2: RunExample(ReplaceSegmentExample, "Replace Segment"); break;
                    case 3: RunExample(RemoveSegmentExample, "Remove Segment"); break;
                    case 4: RunExample(SilenceSegmentExample, "Silence Segment"); break;
                    case 5: RunExample(InsertSegmentExample, "Insert Segment"); break;
                    case 6: RunExample(TrimSegmentExample, "Trim/Crop Segment"); break;
                    case 7: RunExample(DuplicateSegmentExample, "Duplicate Segment"); break;
                    case 8: RunExample(ReverseSegmentExample, "Reverse Segment"); break;
                    case 9: RunExample(LoopSegmentRepetitionsExample, "Loop Repetitions"); break;
                    case 10: RunExample(LoopSegmentDurationExample, "Loop Target Duration"); break;
                    case 11: RunExample(MultiTrackOverlayExample, "Multi-Track & Overlay"); break;
                    case 12: RunExample(VolumeControlExample, "Volume Control"); break;
                    case 13: RunExample(PanControlExample, "Pan Control"); break;
                    case 14: RunExample(FadeInOutExample, "Fade In/Out"); break;
                    case 15: RunExample(MuteTrackExample, "Mute Track"); break;
                    case 16: RunExample(SoloTrackExample, "Solo Track"); break;
                    case 17: RunExample(OffsetSegmentExample, "Offset Segment"); break;
                    case 18: RunExample(SpeedChangeExample, "Speed Change"); break;
                    case 19: RunExample(QueryDurationExample, "Query Duration"); break;
                    case 20: RunExample(RenderCompositionExample, "Render Composition"); break;
                    case 0: running = false; break;
                    default: Console.WriteLine("Invalid choice. Please try again."); break;
                }
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
        Console.WriteLine($"\n--- Running: {exampleName} ---");
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
        Console.WriteLine($"--- Finished: {exampleName} ---");
        Console.WriteLine("Press any key to return to the menu...");
        Console.ReadKey();
        Console.Clear();
    }

    private static void PlayComposition(Composition composition, string message = "Playing composition...")
    {
        Console.WriteLine(message);
        AudioEngine.UpdateAudioDevicesInfo();
        DeviceInfo? deviceInfo = AudioEngine.PlaybackDevices.FirstOrDefault(d => d.IsDefault);
        if (!deviceInfo.HasValue)
        {
            Console.WriteLine("No default playback device found. Cannot play audio.");
            composition.Dispose();
            return;
        }

        using var playbackDevice = AudioEngine.InitializePlaybackDevice(deviceInfo.Value, composition.Format);
        playbackDevice.Start();

        using var player = new SoundPlayer(AudioEngine, composition.Format, composition.Renderer);
        playbackDevice.MasterMixer.AddComponent(player);
        player.Play();

        Console.WriteLine("Press 's' to stop playback early, or wait for it to finish.");
        var compositionDuration = composition.Editor.CalculateTotalDuration();
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
            Console.Write($"\rPlayback time: {elapsed:mm\\:ss\\.f} / {compositionDuration:mm\\:ss\\.f}   ");
            Thread.Sleep(100);
            if (elapsed > compositionDuration + TimeSpan.FromSeconds(1)) 
            {
                break;
            }
        }
        Console.WriteLine("\nPlayback finished or stopped.");
        playbackDevice.MasterMixer.RemoveComponent(player);
        playbackDevice.Stop();
    }

    // Example Implementations

    private static Composition BasicCompositionPlayback()
    {
        var provider = DemoAudio.GenerateShortBeep(TimeSpan.FromSeconds(3));

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");
        var segment1 = new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.Zero, ownsDataProvider: true);
        track1.AddSegment(segment1);
        composition.Editor.AddTrack(track1);
        return composition;
    }

    private static Composition ReplaceSegmentExample()
    {
        var originalProvider = DemoAudio.GenerateLongTone();
        var replacementProvider = DemoAudio.GenerateShortBeep(TimeSpan.FromSeconds(1));

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        track1.AddSegment(new AudioSegment(Format, originalProvider, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.Zero, "Part 1"));

        var segmentToReplaceTimelineStart = TimeSpan.FromSeconds(2);
        var segmentToReplaceDuration = TimeSpan.FromSeconds(1);
        track1.AddSegment(new AudioSegment(Format, originalProvider, TimeSpan.FromSeconds(2), segmentToReplaceDuration, segmentToReplaceTimelineStart, "To Be Replaced"));

        track1.AddSegment(new AudioSegment(Format, originalProvider, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), segmentToReplaceTimelineStart + segmentToReplaceDuration, "Part 3", ownsDataProvider: true));

        composition.Editor.AddTrack(track1);

        Console.WriteLine("Replacing segment from 2s to 3s with a beep.");
        var replaced = composition.Editor.ReplaceSegment(track1,
            segmentToReplaceTimelineStart,
            segmentToReplaceTimelineStart + segmentToReplaceDuration,
            replacementProvider,
            TimeSpan.Zero,
            segmentToReplaceDuration); 

        if (!replaced) Console.WriteLine("Replacement failed. Segment not found.");

        return composition;
    }

    private static Composition RemoveSegmentExample()
    {
        var provider = DemoAudio.GenerateLongTone();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var seg1 = new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.Zero, "Part 1");
        var segToRemove = new AudioSegment(Format, provider, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2), "To Remove", ownsDataProvider: true);
        var seg2 = new AudioSegment(Format, provider, TimeSpan.FromSeconds(3.5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3.5), "Part 2");

        track1.AddSegment(seg1);
        track1.AddSegment(segToRemove);
        track1.AddSegment(seg2);
        composition.Editor.AddTrack(track1);

        Console.WriteLine($"Duration before removal: {composition.Editor.CalculateTotalDuration()}");
        Console.WriteLine("Removing segment 'To Remove' (1.5s long at 2s) and shifting subsequent segments.");
        track1.RemoveSegment(segToRemove, shiftSubsequent: true);
        Console.WriteLine($"Duration after removal: {composition.Editor.CalculateTotalDuration()}");

        composition = new Composition(AudioEngine, Format);
        track1 = new Track("Track 1");
        var p1 = DemoAudio.GenerateLongTone();
        var p2 = DemoAudio.GenerateLongTone();
        var p3 = DemoAudio.GenerateLongTone();
        seg1 = new AudioSegment(Format, p1, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.Zero, "Part 1", ownsDataProvider: true);
        segToRemove = new AudioSegment(Format, p2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(2), "To Remove", ownsDataProvider: true);
        seg2 = new AudioSegment(Format, p3, TimeSpan.FromSeconds(3.5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3.5), "Part 2", ownsDataProvider: true);
        track1.AddSegment(seg1);
        track1.AddSegment(segToRemove);
        track1.AddSegment(seg2);
        composition.Editor.AddTrack(track1);
        track1.RemoveSegment(segToRemove, shiftSubsequent: true); // Now safe to remove

        return composition;
    }


    private static Composition SilenceSegmentExample()
    {
        var provider = DemoAudio.GenerateSpeechFragment();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        track1.AddSegment(new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.Zero, "Speech", ownsDataProvider: true));
        composition.Editor.AddTrack(track1);

        Console.WriteLine("Silencing section from 1.5s to 2.5s by inserting a silent segment.");
        composition.Editor.SilenceSegment(track1, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1));

        return composition;
    }

    private static Composition InsertSegmentExample()
    {
        var mainProvider = DemoAudio.GenerateSpeechFragment();
        var insertProvider = DemoAudio.GenerateShortBeep(TimeSpan.FromSeconds(0.5));

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var part1 = new AudioSegment(Format, mainProvider, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.Zero, "Speech Part 1");
        var part2 = new AudioSegment(Format, mainProvider, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), "Speech Part 2", ownsDataProvider: true);

        track1.AddSegment(part1);
        track1.AddSegment(part2);
        composition.Editor.AddTrack(track1);

        Console.WriteLine($"Duration before insert: {composition.Editor.CalculateTotalDuration()}");
        var segmentToInsert = new AudioSegment(Format, insertProvider, TimeSpan.Zero, TimeSpan.FromSeconds(0.5), TimeSpan.Zero, "Inserted Beep", ownsDataProvider: true);
        track1.InsertSegmentAt(segmentToInsert, TimeSpan.FromSeconds(2), shiftSubsequent: true);
        Console.WriteLine($"Duration after insert: {composition.Editor.CalculateTotalDuration()}");

        return composition;
    }

    private static Composition TrimSegmentExample()
    {
        var provider = DemoAudio.GenerateLongTone();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var trimmedSegment = new AudioSegment(Format, provider,
            sourceStartTime: TimeSpan.FromSeconds(3),
            sourceDuration: TimeSpan.FromSeconds(4),
            timelineStartTime: TimeSpan.Zero,
            name: "Trimmed Middle Part",
            ownsDataProvider: true);
        track1.AddSegment(trimmedSegment);
        composition.Editor.AddTrack(track1);

        Console.WriteLine($"Playing 4s (from 3s to 7s) of generated tone.");
        return composition;
    }

    private static Composition DuplicateSegmentExample()
    {
        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var originalSegment = new AudioSegment(Format, DemoAudio.GenerateFxSound(), TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.Zero, "FX Original", ownsDataProvider: true);
        track1.AddSegment(originalSegment);

        var clonedSegment1 = new AudioSegment(Format, DemoAudio.GenerateFxSound(), TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.5), "FX Clone 1", ownsDataProvider: true);
        track1.AddSegment(clonedSegment1);

        var clonedSegment2 = new AudioSegment(Format, DemoAudio.GenerateFxSound(), TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3.0), "FX Clone 2", ownsDataProvider: true);
        clonedSegment2.Settings.Volume = 0.7f;
        track1.AddSegment(clonedSegment2);

        composition.Editor.AddTrack(track1);
        Console.WriteLine("Playing original FX sound and two clones at different times/volumes.");
        return composition;
    }

    private static Composition ReverseSegmentExample()
    {
        var provider = DemoAudio.GenerateSpeechFragment();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var segmentSettings = new AudioSegmentSettings { IsReversed = true };
        var reversedSegment = new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.Zero, "Reversed Speech", segmentSettings, ownsDataProvider: true);
        track1.AddSegment(reversedSegment);
        composition.Editor.AddTrack(track1);

        Console.WriteLine("Playing first 3 seconds of generated speech in reverse.");
        return composition;
    }

    private static Composition LoopSegmentRepetitionsExample()
    {
        var provider = DemoAudio.GenerateMusicLoop();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var segmentSettings = new AudioSegmentSettings { Loop = new LoopSettings(repetitions: 2) };
        var loopedSegment = new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.Zero, "Looped Music (2 reps)", segmentSettings, ownsDataProvider: true);
        track1.AddSegment(loopedSegment);
        composition.Editor.AddTrack(track1);

        Console.WriteLine("Playing a 2s music loop 3 times (original + 2 repetitions). Total 6s.");
        return composition;
    }

    private static Composition LoopSegmentDurationExample()
    {
        var provider = DemoAudio.GenerateMusicLoop();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var segmentSettings = new AudioSegmentSettings { Loop = new LoopSettings(repetitions: int.MaxValue, targetDuration: TimeSpan.FromSeconds(7)) };
        var loopedSegment = new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.Zero, "Looped Music (target 7s)", segmentSettings, ownsDataProvider: true);
        track1.AddSegment(loopedSegment);
        composition.Editor.AddTrack(track1);

        Console.WriteLine("Playing a 2s music loop to fill a total duration of 7s.");
        return composition;
    }

    private static Composition MultiTrackOverlayExample()
    {
        var speechProvider = DemoAudio.GenerateSpeechFragment();
        var musicProvider = DemoAudio.GenerateMusicLoop();

        var composition = new Composition(AudioEngine, Format);

        var speechTrack = new Track("Speech Track");
        var speechSegment = new AudioSegment(Format, speechProvider, TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.Zero, "Narrator", ownsDataProvider: true);
        speechTrack.AddSegment(speechSegment);
        composition.Editor.AddTrack(speechTrack);

        var musicTrack = new Track("Music Track");
        var musicSettings = new AudioSegmentSettings { Volume = 0.4f, Loop = new LoopSettings(repetitions: int.MaxValue, targetDuration: TimeSpan.FromSeconds(5)) };
        var musicSegment = new AudioSegment(Format, musicProvider, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(0.5), "Background Music", musicSettings, ownsDataProvider: true);
        musicTrack.AddSegment(musicSegment);
        composition.Editor.AddTrack(musicTrack);

        Console.WriteLine("Playing speech overlaid with background music.");
        return composition;
    }

    private static Composition VolumeControlExample()
    {
        var provider = DemoAudio.GenerateLongTone();

        var composition = new Composition(AudioEngine, Format) { MasterVolume = 0.8f };

        var track1 = new Track("Track 1") { Settings = { Volume = 0.9f } };

        var seg1Settings = new AudioSegmentSettings { Volume = 1.2f };
        var seg1 = new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.Zero, "Segment 1 (Loud)", seg1Settings);

        var seg2Settings = new AudioSegmentSettings { Volume = 0.5f };
        var seg2 = new AudioSegment(Format, provider, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), "Segment 2 (Quiet)", seg2Settings, ownsDataProvider: true);

        track1.AddSegment(seg1);
        track1.AddSegment(seg2);
        composition.Editor.AddTrack(track1);

        Console.WriteLine("Demonstrating master, track, and segment volume controls.");
        return composition;
    }

    private static Composition PanControlExample()
    {
        var provider = DemoAudio.GenerateLongTone();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1 Left") { Settings = { Pan = -0.8f } };

        var seg1Settings = new AudioSegmentSettings { Pan = 0.0f };
        var seg1 = new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.Zero, "Seg 1 (Track Left)", seg1Settings);
        track1.AddSegment(seg1);
        composition.Editor.AddTrack(track1);

        var track2 = new Track("Track 2 Right");
        var seg2Settings = new AudioSegmentSettings { Pan = 0.9f };
        var seg2 = new AudioSegment(Format, provider, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1.5), "Seg 2 (Seg Right)", seg2Settings, ownsDataProvider: true);
        track2.AddSegment(seg2);
        composition.Editor.AddTrack(track2);

        Console.WriteLine("Demonstrating track and segment panning.");
        return composition;
    }

    private static Composition FadeInOutExample()
    {
        var provider = DemoAudio.GenerateLongTone();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var segmentSettings = new AudioSegmentSettings
        {
            FadeInDuration = TimeSpan.FromSeconds(1.5),
            FadeInCurve = FadeCurveType.Logarithmic,
            FadeOutDuration = TimeSpan.FromSeconds(2.0),
            FadeOutCurve = FadeCurveType.SCurve
        };
        var segment = new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(6), TimeSpan.Zero, "Fading Segment", segmentSettings, ownsDataProvider: true);
        track1.AddSegment(segment);
        composition.Editor.AddTrack(track1);

        Console.WriteLine("Playing segment with 1.5s Log fade-in and 2s S-Curve fade-out.");
        return composition;
    }

    private static Composition MuteTrackExample()
    {
        var speechProvider = DemoAudio.GenerateSpeechFragment();
        var musicProvider = DemoAudio.GenerateMusicLoop();

        var composition = new Composition(AudioEngine, Format);

        var speechTrack = new Track("Speech Track (Will be Muted)") { Settings = { IsMuted = true } };
        speechTrack.AddSegment(new AudioSegment(Format, speechProvider, TimeSpan.Zero, TimeSpan.FromSeconds(4), TimeSpan.Zero, "Muted Speech", ownsDataProvider: true));
        composition.Editor.AddTrack(speechTrack);

        var musicTrack = new Track("Music Track (Should Play)");
        musicTrack.AddSegment(new AudioSegment(Format, musicProvider, TimeSpan.Zero, TimeSpan.FromSeconds(4), TimeSpan.Zero, "Playing Music", ownsDataProvider: true));
        composition.Editor.AddTrack(musicTrack);

        Console.WriteLine("Speech track is muted, only music should play.");
        return composition;
    }

    private static Composition SoloTrackExample()
    {
        var speechProvider = DemoAudio.GenerateSpeechFragment();
        var musicProvider = DemoAudio.GenerateMusicLoop();
        var fxProvider = DemoAudio.GenerateFxSound();

        var composition = new Composition(AudioEngine, Format);

        var speechTrack = new Track("Speech Track");
        speechTrack.AddSegment(new AudioSegment(Format, speechProvider, TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(0.5), "Speech", ownsDataProvider: true));
        composition.Editor.AddTrack(speechTrack);

        var musicTrack = new Track("Music Track (Will be Soloed)") { Settings = { IsSoloed = true } };
        musicTrack.AddSegment(new AudioSegment(Format, musicProvider, TimeSpan.Zero, TimeSpan.FromSeconds(4), TimeSpan.Zero, "Soloed Music", ownsDataProvider: true));
        composition.Editor.AddTrack(musicTrack);

        var fxTrack = new Track("FX Track");
        fxTrack.AddSegment(new AudioSegment(Format, fxProvider, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1.0), "FX", ownsDataProvider: true));
        composition.Editor.AddTrack(fxTrack);

        Console.WriteLine("Music track is soloed. Only music should play.");
        return composition;
    }

    private static Composition OffsetSegmentExample()
    {
        var provider = DemoAudio.GenerateShortBeep();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var segment = new AudioSegment(Format, provider, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.Zero, "Original Position", ownsDataProvider: true);
        track1.AddSegment(segment);
        Console.WriteLine($"Segment starts at: {segment.TimelineStartTime}");

        segment.TimelineStartTime = TimeSpan.FromSeconds(1.5);
        Console.WriteLine($"Segment new start time: {segment.TimelineStartTime} (offset by 1.5s).");

        composition.Editor.AddTrack(track1);
        return composition;
    }

    private static Composition SpeedChangeExample()
    {
        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");

        var normalSpeedSettings = new AudioSegmentSettings { SpeedFactor = 1.0f };
        var normalSegment = new AudioSegment(Format, DemoAudio.GenerateSpeechFragment(), TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.Zero, "Normal Speed Speech", normalSpeedSettings, ownsDataProvider: true);
        track1.AddSegment(normalSegment);

        var fastSpeedSettings = new AudioSegmentSettings { SpeedFactor = 1.5f };
        var fastSegment = new AudioSegment(Format, DemoAudio.GenerateSpeechFragment(), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "Fast Speed Speech", fastSpeedSettings, ownsDataProvider: true);
        track1.AddSegment(fastSegment);

        var slowSpeedSettings = new AudioSegmentSettings { SpeedFactor = 0.7f };
        var slowSegment = new AudioSegment(Format, DemoAudio.GenerateSpeechFragment(), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2 + (2 / 1.5f)), "Slow Speed Speech", slowSpeedSettings, ownsDataProvider: true);
        track1.AddSegment(slowSegment);

        composition.Editor.AddTrack(track1);
        Console.WriteLine("Playing speech at normal, then 1.5x speed, then 0.7x speed.");
        return composition;
    }

    private static Composition QueryDurationExample()
    {
        var provider1 = DemoAudio.GenerateShortBeep();
        var provider2 = DemoAudio.GenerateLongTone();

        var composition = new Composition(AudioEngine, Format);
        var track1 = new Track("Track 1");
        track1.AddSegment(new AudioSegment(Format, provider1, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.Zero, ownsDataProvider: true));
        track1.AddSegment(new AudioSegment(Format, provider2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2.5), ownsDataProvider: true));
        composition.Editor.AddTrack(track1);

        var track2 = new Track("Track 2");
        var loopedSeg = new AudioSegment(Format, DemoAudio.GenerateShortBeep(), TimeSpan.Zero, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(1),
            settings: new AudioSegmentSettings { Loop = new LoopSettings(repetitions: 2) }, ownsDataProvider: true);
        track2.AddSegment(loopedSeg);
        composition.Editor.AddTrack(track2);

        var totalDuration = composition.Editor.CalculateTotalDuration();
        Console.WriteLine($"Calculated total composition duration: {totalDuration:g} ({(totalDuration.TotalSeconds):F2} seconds)");
        // Don't play this one, just show the duration and return.
        return composition;
    }

    private static Composition RenderCompositionExample()
    {
        var composition = MultiTrackOverlayExample();
        Console.WriteLine("Rendering composition to a float[] buffer...");

        var durationToRender = composition.Editor.CalculateTotalDuration();
        if (durationToRender <= TimeSpan.Zero)
        {
            Console.WriteLine("Composition is empty, nothing to render.");
            return composition;
        }

        var renderedAudio = composition.Renderer.Render(TimeSpan.Zero, durationToRender);
        Console.WriteLine($"Rendered {renderedAudio.Length} samples ({durationToRender.TotalSeconds:F2}s at {composition.Format.SampleRate}Hz, {composition.Format.Channels}ch).");

        Console.WriteLine("Playing rendered audio from float[] buffer:");
        var renderedProvider = new RawDataProvider(renderedAudio);
        var playRenderedComposition = new Composition(AudioEngine, composition.Format);
        var track = new Track("Rendered Track");
        track.AddSegment(new AudioSegment(composition.Format, renderedProvider, TimeSpan.Zero, durationToRender, TimeSpan.Zero, ownsDataProvider: true));
        composition.Editor.AddTrack(track);

        // Dispose the original composition since we are now playing the rendered version
        composition.Dispose();
        
        return playRenderedComposition;
    }
}