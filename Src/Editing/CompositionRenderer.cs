using System.Buffers;
using System.ComponentModel;
using SoundFlow.Interfaces;
using SoundFlow.Enums;
using SoundFlow.Metadata.Models;
using SoundFlow.Midi.Routing.Nodes;
using SoundFlow.Synthesis;

namespace SoundFlow.Editing;

/// <summary>
/// Renders a <see cref="Composition"/> into an audio stream.
/// This class implements <see cref="ISoundDataProvider"/>, allowing a whole composition
/// to be treated as a single audio source for playback or further processing.
/// </summary>
public sealed class CompositionRenderer : ISoundDataProvider
{
    private readonly Composition _composition;
    private int _currentReadPositionSamples;
    private PlaybackState _state = PlaybackState.Stopped;
    private bool _needsReset = true;

    /// <summary>
    /// Gets or sets a value indicating whether the renderer's transport is driven by an external sync source.
    /// When true, the internal clock is bypassed, and playback is advanced via `AdvanceBySyncTicks`.
    /// </summary>
    /// <remarks>
    /// This property shouldn't be set manually. It is managed by the audio engine.
    /// It's exposed as public to be used in external backend implementations.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool IsSyncDriven { get; set; }

    /// <summary>
    /// Gets or sets the start time of the playback loop. If null, looping is disabled.
    /// </summary>
    public TimeSpan? LoopStartTime { get; set; }

    /// <summary>
    /// Gets or sets the end time of the playback loop. If null, looping is disabled.
    /// </summary>
    public TimeSpan? LoopEndTime { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionRenderer"/> class.
    /// </summary>
    /// <param name="composition">The composition instance to be rendered.</param>
    internal CompositionRenderer(Composition composition)
    {
        _composition = composition;
    }
    
    #region Sync Transport Controls

    /// <summary>
    /// Starts playback from the beginning.
    /// </summary>
    /// <remarks>
    /// This method is intended for use by external audio backend implementations to control a sync-driven transport and should not be called directly by the user.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SyncPlay()
    {
        _state = PlaybackState.Playing;
        Seek(0);
    }

    /// <summary>
    /// Stops playback and resets the position to the beginning.
    /// </summary>
    /// <remarks>
    /// This method is intended for use by external audio backend implementations to control a sync-driven transport and should not be called directly by the user.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SyncStop()
    {
        _state = PlaybackState.Stopped;
        Seek(0);
    }
    
    /// <summary>
    /// Resumes playback from the current position.
    /// </summary>
    /// <remarks>
    /// This method is intended for use by external audio backend implementations to control a sync-driven transport and should not be called directly by the user.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SyncContinue()
    {
        _state = PlaybackState.Playing;
    }
    
    /// <summary>
    /// Seeks the playback position to a specific time.
    /// </summary>
    /// <param name="time">The time to seek to.</param>
    /// <remarks>
    /// This method is intended for use by external audio backend implementations to control a sync-driven transport and should not be called directly by the user.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void SyncSeek(TimeSpan time)
    {
        var sampleOffset = (int)(time.TotalSeconds * SampleRate * _composition.TargetChannels);
        Seek(sampleOffset);
    }
    
    /// <summary>
    /// Advances the playback position by a specified number of sync ticks.
    /// This is used when <see cref="IsSyncDriven"/> is true.
    /// </summary>
    /// <param name="tickCount">The number of ticks to advance.</param>
    /// <remarks>
    /// This method is intended for use by external audio backend implementations to control a sync-driven transport and should not be called directly by the user.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AdvanceBySyncTicks(int tickCount)
    {
        if (!IsSyncDriven || _state != PlaybackState.Playing) return;
        
        var samplesPerQuarter = SampleRate * 60.0 / GetTempoAtCurrentPosition().BeatsPerMinute;
        var samplesPerTick = (int)(samplesPerQuarter / _composition.TicksPerQuarterNote);
        
        _currentReadPositionSamples += samplesPerTick * tickCount * _composition.TargetChannels;
    }
    
    /// <summary>
    /// Gets the tempo marker active at the current playback position.
    /// </summary>
    /// <returns>The active <see cref="TempoMarker"/>.</returns>
    /// <remarks>
    /// This method is intended for use by external audio backend implementations to control a sync-driven transport and should not be called directly by the user.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public TempoMarker GetTempoAtCurrentPosition()
    {
        return _composition.Editor.GetTempoAtTime(CurrentTime);
    }

    #endregion
    
    /// <summary>
    /// Renders the full composition into a new float array.
    /// </summary>
    /// <returns>A float array containing the rendered audio samples. An empty array is returned if no samples are rendered.</returns>
    public float[] Render()
    {
        // Render full composition
        return Render(TimeSpan.Zero, _composition.Editor.CalculateTotalDuration());
    }

    /// <summary>
    /// Renders a specific time portion of the composition into a new float array.
    /// </summary>
    /// <param name="startTime">The global timeline start time of the portion to render.</param>
    /// <param name="duration">The duration of the audio to render.</param>
    /// <returns>A float array containing the rendered audio samples. An empty array is returned if no samples are rendered.</returns>
    public float[] Render(TimeSpan startTime, TimeSpan duration)
    {
        var samplesToRender = (int)(duration.TotalSeconds * _composition.SampleRate * _composition.TargetChannels);
        if (samplesToRender <= 0) return [];

        var outputBuffer = new float[samplesToRender];
        Render(startTime, duration, outputBuffer.AsSpan());
        return outputBuffer;
    }

    /// <summary>
    /// Renders a specific time portion of the composition into a provided buffer.
    /// This method mixes audio from all active tracks, applies master volume, and performs clipping.
    /// </summary>
    /// <param name="startTime">The global timeline start time of the portion to render.</param>
    /// <param name="duration">The duration of the audio to render.</param>
    /// <param name="outputBuffer">The span to fill with rendered audio samples. This buffer will be cleared before rendering.</param>
    /// <returns>The number of samples actually written to the output buffer.</returns>
    public int Render(TimeSpan startTime, TimeSpan duration, Span<float> outputBuffer)
    {
        // Handle transport-aware recording triggers (Punch-In/Out).
        if (_composition.Recorder is { IsWaitingForPunchIn: true, PunchInTime: not null } && startTime >= _composition.Recorder.PunchInTime.Value)
            _composition.Recorder.StartInternalRecorders(startTime);
        if (_composition.Recorder is { IsRecording: true, PunchOutTime: not null } && startTime >= _composition.Recorder.PunchOutTime.Value)
            _composition.Recorder.StopRecording();

        var samplesToRender = (int)(duration.TotalSeconds * _composition.SampleRate * _composition.TargetChannels);
        samplesToRender = Math.Min(samplesToRender, outputBuffer.Length);
        if (samplesToRender <= 0) return 0;

        _composition.Recorder.UpdateSampleClock(samplesToRender / _composition.TargetChannels);

        outputBuffer[..samplesToRender].Clear(); // Initialize with silence

        float[]? tempBuffer = null;
        try
        {
            tempBuffer = ArrayPool<float>.Shared.Rent(samplesToRender);
            var tempBufferSpan = tempBuffer.AsSpan(0, samplesToRender);

            // Render audio tracks
            var activeAudioTracks = GetActiveTracksForRendering();
            foreach (var track in activeAudioTracks)
            {
                // Clear the temp buffer for each track
                tempBufferSpan.Clear();
                track.Render(startTime, duration, tempBufferSpan, _composition.SampleRate, _composition.TargetChannels);
                for (var i = 0; i < samplesToRender; i++)
                {
                    outputBuffer[i] += tempBufferSpan[i];
                }
            }
            
            // Render MIDI tracks
            var activeMidiTracks = GetActiveMidiTracksForRendering();
            foreach (var midiTrack in activeMidiTracks)
            {
                // Clear the temp buffer for each track
                tempBufferSpan.Clear();
                midiTrack.Render(startTime, duration, tempBufferSpan);
                for (var i = 0; i < samplesToRender; i++)
                {
                    outputBuffer[i] += tempBufferSpan[i];
                }
            }
        }
        finally
        {
            if (tempBuffer != null)
            {
                ArrayPool<float>.Shared.Return(tempBuffer);
            }
        }

        // Apply Composition (Master) Modifiers
        foreach (var modifier in _composition.Modifiers)
        {
            if (modifier.Enabled)
            {
                modifier.Process(outputBuffer[..samplesToRender], _composition.TargetChannels);
            }
        }

        // Process Composition (Master) Analyzers
        foreach (var analyzer in _composition.Analyzers)
        {
            analyzer.Process(outputBuffer[..samplesToRender], _composition.TargetChannels);
        }

        // Apply master volume and clipping to the final mixed output
        for (var i = 0; i < samplesToRender; i++)
        {
            outputBuffer[i] *= _composition.MasterVolume;
            outputBuffer[i] = Math.Clamp(outputBuffer[i], -1.0f, 1.0f);
        }
        
        return samplesToRender;
    }

    /// <summary>
    /// Determines which tracks are active for rendering based on their mute, solo, and enabled states.
    /// If any track is soloed, only soloed tracks are returned. Otherwise, all non-muted, enabled tracks are returned.
    /// </summary>
    /// <returns>A list of <see cref="Track"/> objects that should be included in the rendering process.</returns>
    private List<Track> GetActiveTracksForRendering()
    {
        var soloedTracks = _composition.Tracks.Where(t => t.Settings is { IsSoloed: true, IsEnabled: true, IsMuted: false }).ToList();
        return soloedTracks.Count != 0 ? soloedTracks : _composition.Tracks.Where(t => t.Settings is { IsMuted: false, IsEnabled: true }).ToList();
    }
    
    private List<MidiTrack> GetActiveMidiTracksForRendering()
    {
        var soloedTracks = _composition.MidiTracks.Where(t => t.Settings is { IsSoloed: true, IsEnabled: true, IsMuted: false }).ToList();
        return soloedTracks.Count != 0 ? soloedTracks : _composition.MidiTracks.Where(t => t.Settings is { IsMuted: false, IsEnabled: true }).ToList();
    }
    
    #region ISoundDataProvider Implementation

    /// <inheritdoc />
    public int Position => _currentReadPositionSamples;
    
    /// <summary>
    /// Gets the current playback time in <see cref="TimeSpan"/>.
    /// </summary>
    public TimeSpan CurrentTime => 
        TimeSpan.FromSeconds((double)_currentReadPositionSamples / SampleRate / _composition.TargetChannels);

    /// <inheritdoc />
    public int Length => (int)(_composition.Editor.CalculateTotalDuration().TotalSeconds * _composition.SampleRate * _composition.TargetChannels);
    
    /// <inheritdoc />
    public bool CanSeek => true;

    /// <inheritdoc />
    public SampleFormat SampleFormat => SampleFormat.F32;
    
    /// <inheritdoc />
    public int SampleRate => _composition.SampleRate;

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }
    
    /// <inheritdoc />
    public SoundFormatInfo? FormatInfo => null;

    /// <inheritdoc />
    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <inheritdoc />
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;

    /// <inheritdoc />
    public int ReadBytes(Span<float> buffer)
    {
        if (IsDisposed) return 0;
        
        // If we are starting playback, reset all synthesizers once.
        if (_needsReset)
        {
            foreach (var midiTargetNode in _composition.MidiTargets)
            {
                if (midiTargetNode is MidiTargetNode { Target: Synthesizer synth })
                {
                    synth.Reset();
                }
            }
            _needsReset = false;
        }
        
        // If not sync-driven, playback is controlled by the consumer calling this method.
        if (!IsSyncDriven)
        {
            _state = PlaybackState.Playing;
        }

        if (_state != PlaybackState.Playing)
        {
            buffer.Clear();
            return buffer.Length;
        }
        
        var currentTime = CurrentTime;
        var durationToRead = TimeSpan.FromSeconds((double)buffer.Length / SampleRate / _composition.TargetChannels);
        var totalDuration = _composition.Editor.CalculateTotalDuration();

        // Handle Looping
        if (LoopStartTime.HasValue && LoopEndTime.HasValue && currentTime >= LoopEndTime.Value)
        {
            var loopDuration = LoopEndTime.Value - LoopStartTime.Value;
            var loopDurationSamples = (long)(loopDuration.TotalSeconds * SampleRate * _composition.TargetChannels);
            
            // Notify the recorder that a transport loop has occurred for correct timestamping.
            _composition.Recorder.OnTransportLoop(loopDurationSamples);

            Seek((int)(LoopStartTime.Value.TotalSeconds * SampleRate * _composition.TargetChannels));
            currentTime = LoopStartTime.Value;
        }

        if (currentTime >= totalDuration)
        {
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
            return 0; // End of composition
        }

        var effectiveEndTime = LoopEndTime ?? totalDuration;
        if (currentTime + durationToRead > effectiveEndTime) 
            durationToRead = effectiveEndTime - currentTime;
            
        var samplesWritten = Render(currentTime, durationToRead, buffer);
            
        _currentReadPositionSamples += samplesWritten;
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(_currentReadPositionSamples));

        if (samplesWritten < buffer.Length || currentTime + durationToRead >= totalDuration) 
            EndOfStreamReached?.Invoke(this, EventArgs.Empty);
        return samplesWritten;
    }

    /// <inheritdoc />
    public void Seek(int sampleOffset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        
        _currentReadPositionSamples = Math.Clamp(sampleOffset, 0, Length);
        
        if (_currentReadPositionSamples == 0) _needsReset = true;
        
        // Signal to all segments that their internal read state is now invalid because of this time jump.
        foreach (var track in _composition.Tracks)
        {
            foreach (var segment in track.Segments)
            {
                segment.InvalidateReadState();
            }
        }
        
        PositionChanged?.Invoke(this, new PositionChangedEventArgs(_currentReadPositionSamples));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
    }
    
    #endregion
}