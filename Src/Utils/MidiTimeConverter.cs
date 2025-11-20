using SoundFlow.Editing;

namespace SoundFlow.Utils;

/// <summary>
/// A static utility class providing methods to convert between MIDI ticks and real time (TimeSpan)
/// based on a composition's tempo map.
/// </summary>
public static class MidiTimeConverter
{
    /// <summary>
    /// Converts an absolute time in MIDI ticks to a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="tick">The absolute tick position to convert.</param>
    /// <param name="ticksPerQuarterNote">The time division of the sequence.</param>
    /// <param name="tempoTrack">The composition's master tempo track.</param>
    /// <returns>The corresponding TimeSpan from the beginning of the sequence.</returns>
    public static TimeSpan GetTimeSpanForTick(long tick, int ticksPerQuarterNote, IReadOnlyList<TempoMarker> tempoTrack)
    {
        if (tick <= 0) return TimeSpan.Zero;

        double timeInSeconds = 0;
        long lastTick = 0;
        var currentBpm = 120.0; // Default MIDI tempo

        foreach (var marker in tempoTrack)
        {
            var markerTick = GetTickForTimeSpan(marker.Time, ticksPerQuarterNote, tempoTrack, currentBpm, lastTick, timeInSeconds);
            if (tick <= markerTick)
            {
                var ticksInSegment = tick - lastTick;
                var secondsPerTick = 60.0 / (currentBpm * ticksPerQuarterNote);
                timeInSeconds += ticksInSegment * secondsPerTick;
                return TimeSpan.FromSeconds(timeInSeconds);
            }

            var segmentTicks = markerTick - lastTick;
            timeInSeconds += (60.0 / (currentBpm * ticksPerQuarterNote)) * segmentTicks;

            lastTick = markerTick;
            currentBpm = marker.BeatsPerMinute;
        }

        // If we get here, the tick is beyond the last tempo change
        var remainingTicks = tick - lastTick;
        timeInSeconds += (60.0 / (currentBpm * ticksPerQuarterNote)) * remainingTicks;

        return TimeSpan.FromSeconds(timeInSeconds);
    }

    /// <summary>
    /// Converts a <see cref="TimeSpan"/> to an absolute time in MIDI ticks.
    /// </summary>
    /// <param name="time">The TimeSpan to convert.</param>
    /// <param name="ticksPerQuarterNote">The time division of the sequence.</param>
    /// <param name="tempoTrack">The composition's master tempo track.</param>
    /// <returns>The corresponding absolute tick position.</returns>
    public static long GetTickForTimeSpan(TimeSpan time, int ticksPerQuarterNote, IReadOnlyList<TempoMarker> tempoTrack)
    {
        return GetTickForTimeSpan(time, ticksPerQuarterNote, tempoTrack, 120.0, 0, 0);
    }
    
    private static long GetTickForTimeSpan(TimeSpan time, int ticksPerQuarterNote, IReadOnlyList<TempoMarker> tempoTrack, double initialBpm, long initialTick, double initialTime)
    {
        if (time <= TimeSpan.Zero) return 0;

        var timeInSeconds = time.TotalSeconds;
        var totalTicks = initialTick;
        var accumulatedTime = initialTime;
        var currentBpm = initialBpm;

        foreach (var marker in tempoTrack.Where(m => m.Time > TimeSpan.FromSeconds(initialTime)))
        {
            var timeToNextMarker = (marker.Time - TimeSpan.FromSeconds(accumulatedTime)).TotalSeconds;
            var ticksAtCurrentTempo = timeToNextMarker * (currentBpm * ticksPerQuarterNote / 60.0);

            if (accumulatedTime + timeToNextMarker >= timeInSeconds)
            {
                var remainingTime = timeInSeconds - accumulatedTime;
                totalTicks += (long)(remainingTime * (currentBpm * ticksPerQuarterNote / 60.0));
                return totalTicks;
            }

            accumulatedTime += timeToNextMarker;
            totalTicks += (long)ticksAtCurrentTempo;
            currentBpm = marker.BeatsPerMinute;
        }

        // If time is beyond the last tempo change
        var timeAfterLastPoint = timeInSeconds - accumulatedTime;
        totalTicks += (long)(timeAfterLastPoint * (currentBpm * ticksPerQuarterNote / 60.0));

        return totalTicks;
    }
}