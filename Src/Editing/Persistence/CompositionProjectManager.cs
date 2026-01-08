using System.Buffers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SoundFlow.Abstracts;
using SoundFlow.Editing.Mapping;
using SoundFlow.Enums;
using SoundFlow.Interfaces;
using SoundFlow.Metadata.Midi;
using SoundFlow.Midi.Abstracts;
using SoundFlow.Midi.Interfaces;
using SoundFlow.Midi.Routing.Nodes;
using SoundFlow.Providers;
using SoundFlow.Security;
using SoundFlow.Security.Configuration;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Manages the saving and loading of audio composition projects.
/// </summary>
public static class CompositionProjectManager
{
    // The native file version this version of the library is designed to write and read.
    // Used for compatibility checks during loading.
    private const string DefaultProjectFileVersion = "1.4.0";

    #region Saving

    /// <summary>
    /// Saves the given composition to the specified project file path using the specified options.
    /// </summary>
    /// <param name="engine">The audio engine instance of the composition.</param>
    /// <param name="composition">The composition to save.</param>
    /// <param name="projectFilePath">The full path where the project file will be saved.</param>
    /// <param name="options">Configuration options for the save operation. If null, default options will be used.</param>
    /// <param name="customTypeResolver">
    /// An optional JSON type resolver for user-defined types. 
    /// Required if your project contains custom Modifiers or Analyzers in a NativeAOT environment.
    /// </param>
    public static async Task SaveProjectAsync(
        AudioEngine engine,
        Composition composition,
        string projectFilePath,
        ProjectSaveOptions? options = null,
        IJsonTypeInfoResolver? customTypeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrEmpty(projectFilePath);

        // If no options are provided, create a default instance to avoid null checks everywhere.
        options ??= new ProjectSaveOptions();

        // Combine internal context with optional user context
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = customTypeResolver != null
                ? JsonTypeInfoResolver.Combine(SoundFlowJsonContext.Default, customTypeResolver)
                : SoundFlowJsonContext.Default
        };
        
        var projectData = new ProjectData
        {
            ProjectFileVersion = options.ProjectFileVersion,
            Name = composition.Name,
            MasterVolume = composition.MasterVolume,
            TargetSampleRate = composition.SampleRate,
            TargetChannels = composition.TargetChannels,
            TicksPerQuarterNote = composition.TicksPerQuarterNote,
            TempoTrack = composition.TempoTrack.Select(m => new ProjectTempoMarker
                { Time = m.Time, BeatsPerMinute = m.BeatsPerMinute }).ToList(),
            Modifiers = SerializeEffects(composition.Modifiers, jsonOptions),
            Analyzers = SerializeEffects(composition.Analyzers, jsonOptions),
            MidiTargets = SerializeEffects(composition.MidiTargets.OfType<MidiTargetNode>().Select(n => n.Target), jsonOptions),
            MidiMappings = SerializeMappings(composition.MappingManager.Mappings)
        };

        var projectDirectory = Path.GetDirectoryName(projectFilePath)
                               ?? throw new IOException("Invalid project file path.");
        var mediaAssetsDirectory = Path.Combine(projectDirectory, options.ConsolidatedMediaFolderName);

        if (options.ConsolidateMedia) Directory.CreateDirectory(mediaAssetsDirectory);

        var sourceProviderMap = new Dictionary<ISoundDataProvider, ProjectSourceReference>();
        var midiSequenceMap = new Dictionary<MidiSequence, ProjectSourceReference>();

        foreach (var track in composition.Tracks)
        {
            var projectTrack = new ProjectTrack
            {
                Name = track.Name,
                Settings = new ProjectTrackSettings
                {
                    IsEnabled = track.Settings.IsEnabled,
                    IsMuted = track.Settings.IsMuted,
                    IsSoloed = track.Settings.IsSoloed,
                    Volume = track.Settings.Volume,
                    Pan = track.Settings.Pan,
                    Modifiers = SerializeEffects(track.Settings.Modifiers, jsonOptions),
                    Analyzers = SerializeEffects(track.Settings.Analyzers, jsonOptions)
                }
            };

            foreach (var segment in track.Segments)
            {
                if (!sourceProviderMap.TryGetValue(segment.SourceDataProvider, out var sourceRef))
                {
                    sourceRef = await CreateSourceReferenceAsync(
                        segment.SourceDataProvider,
                        engine,
                        projectDirectory,
                        mediaAssetsDirectory,
                        options
                    );
                    sourceProviderMap[segment.SourceDataProvider] = sourceRef;
                    if (projectData.SourceReferences.All(sr => sr.Id != sourceRef.Id))
                    {
                        projectData.SourceReferences.Add(sourceRef);
                    }
                    else
                    {
                        sourceRef = projectData.SourceReferences.First(sr => sr.Id == sourceRef.Id);
                        sourceProviderMap[segment.SourceDataProvider] = sourceRef;
                    }
                }

                projectTrack.Segments.Add(new ProjectSegment
                {
                    Name = segment.Name,
                    SourceReferenceId = sourceRef.Id,
                    SourceStartTime = segment.SourceStartTime,
                    SourceDuration = segment.SourceDuration,
                    TimelineStartTime = segment.TimelineStartTime,
                    Settings = new ProjectAudioSegmentSettings
                    {
                        IsEnabled = segment.Settings.IsEnabled,
                        Loop = segment.Settings.Loop,
                        IsReversed = segment.Settings.IsReversed,
                        Volume = segment.Settings.Volume,
                        Pan = segment.Settings.Pan,
                        SpeedFactor = segment.Settings.SpeedFactor,
                        FadeInDuration = segment.Settings.FadeInDuration,
                        FadeOutDuration = segment.Settings.FadeOutDuration,
                        FadeInCurve = segment.Settings.FadeInCurve,
                        FadeOutCurve = segment.Settings.FadeOutCurve,
                        TimeStretchFactor = segment.Settings.TimeStretchFactor,
                        TargetStretchDuration = segment.Settings.TargetStretchDuration,
                        Modifiers = SerializeEffects(segment.Settings.Modifiers, jsonOptions),
                        Analyzers = SerializeEffects(segment.Settings.Analyzers, jsonOptions)
                    }
                });
            }

            projectData.Tracks.Add(projectTrack);
        }

        foreach (var midiTrack in composition.MidiTracks)
        {
            var projectMidiTrack = new ProjectMidiTrack
            {
                Name = midiTrack.Name,
                TargetComponentName = midiTrack.Target?.Name,
                Settings = new ProjectTrackSettings
                {
                    IsEnabled = midiTrack.Settings.IsEnabled,
                    IsMuted = midiTrack.Settings.IsMuted,
                    IsSoloed = midiTrack.Settings.IsSoloed,
                    MidiModifiers = SerializeEffects(midiTrack.Settings.MidiModifiers, jsonOptions)
                }
            };

            foreach (var segment in midiTrack.Segments)
            {
                if (!midiSequenceMap.TryGetValue(segment.Sequence, out var sourceRef))
                {
                    sourceRef = await CreateMidiSourceReferenceAsync(segment.Sequence, projectDirectory,
                        mediaAssetsDirectory);
                    midiSequenceMap[segment.Sequence] = sourceRef;
                    projectData.SourceReferences.Add(sourceRef);
                }

                projectMidiTrack.Segments.Add(new ProjectMidiSegment
                {
                    Name = segment.Name,
                    SourceReferenceId = sourceRef.Id,
                    TimelineStartTime = segment.TimelineStartTime
                });
            }

            projectData.MidiTracks.Add(projectMidiTrack);
        }

        var typeInfo = (JsonTypeInfo<ProjectData>)jsonOptions.GetTypeInfo(typeof(ProjectData));
        var json = JsonSerializer.Serialize(projectData, typeInfo);
        await File.WriteAllTextAsync(projectFilePath, json);

        // Signing Integration
        if (options.SigningConfiguration != null)
        {
            var sigResult = await FileAuthenticator.SignFileAsync(projectFilePath, options.SigningConfiguration);
            if (sigResult.IsSuccess)
            {
                var sigPath = projectFilePath + ".sig";
                await File.WriteAllTextAsync(sigPath, sigResult.Value);
            }
            else
            {
                Log.Warning($"Failed to sign project file: {sigResult.Error?.Message}");
            }
        }

        composition.ClearDirtyFlag();
    }

    /// <summary>
    /// Verifies the integrity and authenticity of a project file using a detached digital signature.
    /// This method checks if the project file has been modified since it was signed.
    /// </summary>
    /// <param name="projectFilePath">The path to the project file (.sfproj).</param>
    /// <param name="signatureFilePath">The path to the signature file (.sig). If null, defaults to projectFilePath + ".sig".</param>
    /// <param name="config">The signature configuration containing the Public Key.</param>
    /// <returns>A result containing true if the project is valid and authentic; otherwise, false or an error.</returns>
    public static async Task<Result<bool>> VerifyProjectAsync(string projectFilePath, string? signatureFilePath, SignatureConfiguration config)
    {
        if (!File.Exists(projectFilePath))
            return new NotFoundError("File", $"Project file not found: {projectFilePath}");

        var sigPath = signatureFilePath ?? projectFilePath + ".sig";
        if (!File.Exists(sigPath))
            return new NotFoundError("File", $"Signature file not found: {sigPath}");

        try
        {
            var signature = await File.ReadAllTextAsync(sigPath);
            return await FileAuthenticator.VerifyFileAsync(projectFilePath, signature, config);
        }
        catch (Exception ex)
        {
            return new Error("An error occurred while verifying the project file.", ex);
        }
    }

    private static async Task<ProjectSourceReference> CreateSourceReferenceAsync(
        ISoundDataProvider provider,
        AudioEngine engine,
        string projectDirectory,
        string mediaAssetsDirectory,
        ProjectSaveOptions options)
    {
        var sourceFormat = new AudioFormat
        {
            Format = provider.SampleFormat,
            SampleRate = provider.SampleRate,
            Channels = provider.FormatInfo?.ChannelCount ?? 2,
            Layout = AudioFormat.GetLayoutFromChannels(provider.FormatInfo?.ChannelCount ?? 2)
        };

        var sourceRef = new ProjectSourceReference
        {
            OriginalSampleFormat = sourceFormat.Format,
            OriginalSampleRate = sourceFormat.SampleRate,
            OriginalChannelLayout = sourceFormat.Layout
        };

        // 1. Attempt Embedding (if enabled and suitable)
        // Provider must be seekable and have a known, finite length for embedding.
        if (options.EmbedSmallMedia && provider is { CanSeek: true, Length: > 0 } &&
            (long)provider.Length * provider.SampleFormat.GetBytesPerSample() < options.MaxEmbedSizeBytes)
        {
            var samplesToEmbed = provider.Length;
            if (samplesToEmbed > 0)
            {
                var tempBuffer = ArrayPool<float>.Shared.Rent(samplesToEmbed);
                try
                {
                    provider.Seek(0);
                    var readCount = provider.ReadBytes(tempBuffer.AsSpan(0, samplesToEmbed));
                    if (readCount == samplesToEmbed)
                    {
                        var byteBuffer = new byte[readCount * sizeof(float)];
                        Buffer.BlockCopy(tempBuffer, 0, byteBuffer, 0, byteBuffer.Length);
                        sourceRef.EmbeddedDataB64 = Convert.ToBase64String(byteBuffer);
                        sourceRef.IsEmbedded = true;
                        sourceRef.OriginalSampleFormat = SampleFormat.F32;
                        sourceRef.OriginalSampleRate = provider.SampleRate;
                        return sourceRef;
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(tempBuffer);
                }
            }
        }

        // 2. Attempt Consolidation (if enabled and not embedded)
        // Requires the provider to be fully readable (seekable, known length).
        if (options.ConsolidateMedia && provider is { CanSeek: true, Length: > 0 })
        {
            var consolidatedFileName = $"{sourceRef.Id:N}.wav";
            var consolidatedFilePath = Path.Combine(mediaAssetsDirectory, consolidatedFileName);

            // Check if this exact source (by ID) has already been consolidated.
            if (!File.Exists(consolidatedFilePath))
            {
                var totalSamples = provider.Length;
                var tempBuffer = ArrayPool<float>.Shared.Rent(totalSamples);
                try
                {
                    provider.Seek(0); // Read from the beginning
                    var samplesRead = provider.ReadBytes(tempBuffer.AsSpan(0, totalSamples));

                    if (samplesRead == totalSamples && totalSamples > 0)
                    {
                        // Use the source provider's own format for consolidation.
                        var audioFormatForEncoding = new AudioFormat
                        {
                            Format = SampleFormat.F32,
                            Channels = sourceFormat.Channels,
                            Layout = sourceFormat.Layout,
                            SampleRate = sourceFormat.SampleRate
                        };

                        var stream = new FileStream(consolidatedFilePath, FileMode.Create, FileAccess.Write,
                            FileShare.None, bufferSize: 4096);
                        var encoder = engine.CreateEncoder(stream, "wav", audioFormatForEncoding);
                        encoder.Encode(tempBuffer.AsSpan(0, samplesRead));
                        encoder.Dispose();
                        await stream.DisposeAsync();
                    }
                    else if (totalSamples == 0)
                    {
                        // Handle empty provider, create an empty WAV file or skip consolidation for it.
                        await File.WriteAllBytesAsync(consolidatedFilePath,
                            CreateEmptyWavHeader(sourceFormat.SampleRate, sourceFormat.Channels, SampleFormat.F32));
                    }
                    else
                    {
                        Log.Warning(
                            $"Could not read all samples from in-memory provider for consolidation (ID: {sourceRef.Id}). Expected {totalSamples}, got {samplesRead}.");
                        return sourceRef; // Return without consolidated path
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(tempBuffer);
                }
            }

            sourceRef.ConsolidatedRelativePath = Path.GetRelativePath(projectDirectory, consolidatedFilePath)
                .Replace(Path.DirectorySeparatorChar, '/');
            return sourceRef;
        }

        // 3. If not embedded and not consolidated (or consolidation failed for in-memory), return a placeholder.
        return sourceRef;
    }

    private static async Task<ProjectSourceReference> CreateMidiSourceReferenceAsync(MidiSequence sequence,
        string projectDirectory, string mediaAssetsDirectory)
    {
        var sourceRef = new ProjectSourceReference
        {
            IsMidiData = true
        };

        var consolidatedFileName = $"{sourceRef.Id:N}.mid";
        var consolidatedFilePath = Path.Combine(mediaAssetsDirectory, consolidatedFileName);

        var midiFile = sequence.ToMidiFile();

        await using (var fileStream =
                     new FileStream(consolidatedFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            MidiFileWriter.Write(midiFile, fileStream);
        }

        sourceRef.ConsolidatedRelativePath = Path.GetRelativePath(projectDirectory, consolidatedFilePath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return sourceRef;
    }


    // Basic WAV header for an empty F32 file.
    private static byte[] CreateEmptyWavHeader(int sampleRate, int channels, SampleFormat format)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        var bytesPerSample = format.GetBytesPerSample();
        int blockAlign = (short)(channels * bytesPerSample);
        var averageBytesPerSecond = sampleRate * blockAlign;

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36); // ChunkSize (36 + 0 data bytes)
        writer.Write("WAVE"u8);

        // Sub-chunk 1 "fmt "
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)(format == SampleFormat.F32 ? 3 : 1)); // AudioFormat (3 for IEEE float, 1 for PCM)
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(averageBytesPerSecond);
        writer.Write((short)blockAlign);
        writer.Write((short)(bytesPerSample * 8));

        // Sub-chunk 2 "data"
        writer.Write("data"u8);
        writer.Write(0);

        return ms.ToArray();
    }

    #endregion

    #region Loading

    /// <summary>
    /// Loads a composition from the specified project file path.
    /// </summary>
    /// <param name="engine">The audio engine instance for context.</param>
    /// <param name="format">The audio format of the composition. Cannot be null.</param>
    /// <param name="projectFilePath">The full path of the project file to load.</param>
    /// <param name="customTypeResolver">
    /// An optional JSON type resolver for user-defined types. 
    /// Required if your project contains custom Modifiers or Analyzers in a NativeAOT environment.
    /// </param>
    /// <returns>A tuple containing the loaded Composition and a list of missing/unresolved source references.</returns>
    public static async Task<(Composition Composition, List<ProjectSourceReference> UnresolvedSources)>
        LoadProjectAsync(
            AudioEngine engine, 
            AudioFormat format, 
            string projectFilePath,
            IJsonTypeInfoResolver? customTypeResolver = null)
    {
        if (!File.Exists(projectFilePath))
            throw new FileNotFoundException("Project file not found.", projectFilePath);

        // Combine internal context with optional user context
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = customTypeResolver != null
                ? JsonTypeInfoResolver.Combine(SoundFlowJsonContext.Default, customTypeResolver)
                : SoundFlowJsonContext.Default
        };

        var json = await File.ReadAllTextAsync(projectFilePath);
        var typeInfo = (JsonTypeInfo<ProjectData>)jsonOptions.GetTypeInfo(typeof(ProjectData));
        var projectData = JsonSerializer.Deserialize(json, typeInfo)
                          ?? throw new JsonException("Failed to deserialize project data.");

        if (Version.TryParse(projectData.ProjectFileVersion, out var fileVersion) &&
            Version.TryParse(DefaultProjectFileVersion, out var currentVersion) &&
            fileVersion.Major > currentVersion.Major)
        {
            Log.Warning(
                $"Loading project file version {projectData.ProjectFileVersion} with library version {DefaultProjectFileVersion}. Forward compatibility is not guaranteed.");
        }

        var composition = new Composition(engine, format, projectData.Name)
        {
            MasterVolume = projectData.MasterVolume,
            SampleRate = projectData.TargetSampleRate,
            TargetChannels = projectData.TargetChannels,
            TicksPerQuarterNote = projectData.TicksPerQuarterNote
        };
        composition.TempoTrack.Clear();
        composition.TempoTrack.AddRange(projectData.TempoTrack.Select(m => new TempoMarker(m.Time, m.BeatsPerMinute)));
        composition.Modifiers.AddRange(DeserializeEffects<SoundModifier>(format, projectData.Modifiers, composition, jsonOptions));
        composition.Analyzers.AddRange(DeserializeEffects<AudioAnalyzer>(format, projectData.Analyzers, composition, jsonOptions));
        
        var deserializedMidiControllables = DeserializeEffects<IMidiControllable>(format, projectData.MidiTargets, composition, jsonOptions);
        composition.MidiTargets.AddRange(deserializedMidiControllables.Select(c => new MidiTargetNode(c)));


        var projectDirectory = Path.GetDirectoryName(projectFilePath)
                               ?? throw new IOException("Invalid project file path.");
        var unresolvedSources = new List<ProjectSourceReference>();

        var resolvedProviderCache = new Dictionary<Guid, ISoundDataProvider>();
        var resolvedMidiSequenceCache = new Dictionary<Guid, MidiSequence>();

        foreach (var sourceRef in projectData.SourceReferences)
        {
            if (sourceRef.IsMidiData)
            {
                if (!resolvedMidiSequenceCache.ContainsKey(sourceRef.Id))
                {
                    var sequence = ResolveMidiSourceReference(sourceRef, projectDirectory);
                    if (sequence != null)
                    {
                        resolvedMidiSequenceCache[sourceRef.Id] = sequence;
                    }
                    else
                    {
                        sourceRef.IsMissing = true;
                        unresolvedSources.Add(sourceRef);
                    }
                }
            }
            else
            {
                if (!resolvedProviderCache.TryGetValue(sourceRef.Id, out var value))
                {
                    sourceRef.ResolvedDataProvider = ResolveSourceReference(engine, sourceRef, projectDirectory);
                    if (sourceRef.ResolvedDataProvider != null)
                    {
                        resolvedProviderCache[sourceRef.Id] = sourceRef.ResolvedDataProvider;
                    }
                    else
                    {
                        sourceRef.IsMissing = true;
                        unresolvedSources.Add(sourceRef);
                    }
                }
                else
                {
                    sourceRef.ResolvedDataProvider = value;
                    sourceRef.IsMissing = false;
                }
            }
        }

        foreach (var projectTrack in projectData.Tracks)
        {
            var trackSettings = new TrackSettings
            {
                IsEnabled = projectTrack.Settings.IsEnabled,
                IsMuted = projectTrack.Settings.IsMuted,
                IsSoloed = projectTrack.Settings.IsSoloed,
                Volume = projectTrack.Settings.Volume,
                Pan = projectTrack.Settings.Pan,
            };
            trackSettings.Modifiers.AddRange(
                DeserializeEffects<SoundModifier>(format, projectTrack.Settings.Modifiers, composition, jsonOptions));
            trackSettings.Analyzers.AddRange(
                DeserializeEffects<AudioAnalyzer>(format, projectTrack.Settings.Analyzers, composition, jsonOptions));

            var track = new Track(projectTrack.Name, trackSettings)
            {
                ParentComposition = composition
            };

            foreach (var projectSegment in projectTrack.Segments)
            {
                var sourceRef =
                    projectData.SourceReferences.FirstOrDefault(sr => sr.Id == projectSegment.SourceReferenceId);
                var providerToUse = sourceRef?.ResolvedDataProvider;

                if (providerToUse == null)
                {
                    Log.Warning(
                        $"Audio source for segment '{projectSegment.Name}' (Ref ID: {projectSegment.SourceReferenceId}) is missing. Using placeholder.");
                    var silentDuration = projectSegment.SourceDuration;
                    var placeholderSampleCount = (int)(Math.Max(silentDuration.TotalSeconds, 0.1) *
                                                       composition.SampleRate * composition.TargetChannels);
                    providerToUse = new RawDataProvider(new float[placeholderSampleCount]);
                }

                var segmentSettings = new AudioSegmentSettings
                {
                    IsEnabled = projectSegment.Settings.IsEnabled,
                    Loop = projectSegment.Settings.Loop,
                    IsReversed = projectSegment.Settings.IsReversed,
                    Volume = projectSegment.Settings.Volume,
                    Pan = projectSegment.Settings.Pan,
                    SpeedFactor = projectSegment.Settings.SpeedFactor,
                    FadeInDuration = projectSegment.Settings.FadeInDuration,
                    FadeOutDuration = projectSegment.Settings.FadeOutDuration,
                    FadeInCurve = projectSegment.Settings.FadeInCurve,
                    FadeOutCurve = projectSegment.Settings.FadeOutCurve,
                };
                segmentSettings.Modifiers.AddRange(
                    DeserializeEffects<SoundModifier>(format, projectSegment.Settings.Modifiers, composition, jsonOptions));
                segmentSettings.Analyzers.AddRange(
                    DeserializeEffects<AudioAnalyzer>(format, projectSegment.Settings.Analyzers, composition, jsonOptions));

                var segment = new AudioSegment(
                    format,
                    providerToUse,
                    projectSegment.SourceStartTime,
                    projectSegment.SourceDuration,
                    projectSegment.TimelineStartTime,
                    projectSegment.Name,
                    segmentSettings,
                    ownsDataProvider: true
                )
                {
                    ParentTrack = track
                };
                if (projectSegment.Settings.TargetStretchDuration.HasValue)
                    segment.Settings.TargetStretchDuration = projectSegment.Settings.TargetStretchDuration;
                else
                    segment.Settings.TimeStretchFactor = projectSegment.Settings.TimeStretchFactor;

                track.AddSegment(segment);
            }

            composition.Editor.AddTrack(track);
        }

        foreach (var projectMidiTrack in projectData.MidiTracks)
        {
            var trackSettings = new TrackSettings
            {
                IsEnabled = projectMidiTrack.Settings.IsEnabled,
                IsMuted = projectMidiTrack.Settings.IsMuted,
                IsSoloed = projectMidiTrack.Settings.IsSoloed
            };
            trackSettings.MidiModifiers.AddRange(DeserializeEffects<MidiModifier>(default,
                projectMidiTrack.Settings.MidiModifiers, composition, jsonOptions));

            var midiTrack = new MidiTrack(projectMidiTrack.Name, settings: trackSettings)
            {
                ParentComposition = composition
            };

            foreach (var projectSegment in projectMidiTrack.Segments)
            {
                if (resolvedMidiSequenceCache.TryGetValue(projectSegment.SourceReferenceId, out var sequence))
                {
                    var segment = new MidiSegment(sequence, projectSegment.TimelineStartTime, projectSegment.Name);
                    midiTrack.AddSegment(segment);
                }
                else
                {
                    Log.Warning(
                        $"MIDI source for segment '{projectSegment.Name}' (Ref ID: {projectSegment.SourceReferenceId}) is missing. Segment skipped.");
                }
            }

            composition.Editor.AddMidiTrack(midiTrack);
        }

        // Post-load step: Link MIDI track targets
        foreach (var projectMidiTrack in projectData.MidiTracks)
        {
            if (string.IsNullOrEmpty(projectMidiTrack.TargetComponentName)) continue;

            var midiTrack = composition.MidiTracks.FirstOrDefault(t => t.Name == projectMidiTrack.Name);
            if (midiTrack == null) continue;

            // Try to find the target in the internal list first.
            var targetNode = composition.MidiTargets.FirstOrDefault(t => t.Name == projectMidiTrack.TargetComponentName);

            // If not found, it might be a physical output device.
            if (targetNode == null)
            {
                var outputDevice = engine.MidiOutputDevices.FirstOrDefault(d => d.Name == projectMidiTrack.TargetComponentName);
                if (outputDevice.Name != null)
                {
                    // This is complex. The MidiManager owns device nodes. We need a way to request one.
                    // For now, this part of linking is a known limitation if not an internal target.
                    Log.Warning($"Could not find MIDI output device '{projectMidiTrack.TargetComponentName}' to link to track '{midiTrack.Name}'.");
                }
            }
            midiTrack.Target = targetNode;
        }

        // Post-load step: Recreate MIDI Mappings
        DeserializeMappings(projectData.MidiMappings, composition);

        composition.ClearDirtyFlag();
        return (composition, unresolvedSources);
    }

    private static ISoundDataProvider? ResolveSourceReference(AudioEngine engine, ProjectSourceReference sourceRef,
        string projectDirectory)
    {
        // 1. Try Embedded
        if (sourceRef.IsEmbedded && !string.IsNullOrEmpty(sourceRef.EmbeddedDataB64))
        {
            try
            {
                var byteBuffer = Convert.FromBase64String(sourceRef.EmbeddedDataB64);
                // Embedded data is saved as F32 float array samples
                if (sourceRef.OriginalSampleFormat == SampleFormat.F32)
                {
                    var floatArray = new float[byteBuffer.Length / sizeof(float)];
                    Buffer.BlockCopy(byteBuffer, 0, floatArray, 0, byteBuffer.Length);
                    return new RawDataProvider(floatArray);
                }

                Log.Warning(
                    $"Unsupported embedded format {sourceRef.OriginalSampleFormat} for source ID {sourceRef.Id}");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"Error decoding embedded data for source ID {sourceRef.Id}: {ex.Message}");
                return null;
            }
        }

        // For file-based sources, we need to create an AudioFormat based on the saved metadata.
        var format = new AudioFormat
        {
            Format = sourceRef.OriginalSampleFormat,
            SampleRate = sourceRef.OriginalSampleRate ?? 48000, // Fallback, though it should exist
            Layout = sourceRef.OriginalChannelLayout,
            Channels = sourceRef.OriginalChannelLayout switch
            {
                ChannelLayout.Mono => 1,
                ChannelLayout.Stereo => 2,
                ChannelLayout.Quad => 4,
                ChannelLayout.Surround51 => 6,
                ChannelLayout.Surround71 => 8,
                _ => 2
            }
        };

        string? pathToTry;

        // 2. Try Consolidated Relative Path
        if (!string.IsNullOrEmpty(sourceRef.ConsolidatedRelativePath))
        {
            pathToTry = Path.GetFullPath(Path.Combine(projectDirectory, sourceRef.ConsolidatedRelativePath));
            if (File.Exists(pathToTry))
                return new StreamDataProvider(engine, format,
                    new FileStream(pathToTry, FileMode.Open, FileAccess.Read, FileShare.Read));

            Log.Warning($"Consolidated file not found for source ID {sourceRef.Id} at expected path: {pathToTry}");
        }

        // 3. Try Original Absolute Path
        if (!string.IsNullOrEmpty(sourceRef.OriginalAbsolutePath))
        {
            pathToTry = sourceRef.OriginalAbsolutePath;
            if (File.Exists(pathToTry))
                return new StreamDataProvider(engine, format,
                    new FileStream(pathToTry, FileMode.Open, FileAccess.Read, FileShare.Read));

            Log.Warning($"Original absolute file not found for source ID {sourceRef.Id} at path: {pathToTry}");
        }

        // If all attempts fail
        return null;
    }

    private static MidiSequence? ResolveMidiSourceReference(ProjectSourceReference sourceRef, string projectDirectory)
    {
        if (string.IsNullOrEmpty(sourceRef.ConsolidatedRelativePath)) return null;

        var filePath = Path.GetFullPath(Path.Combine(projectDirectory, sourceRef.ConsolidatedRelativePath));
        if (!File.Exists(filePath))
        {
            Log.Warning($"Consolidated MIDI file not found for source ID {sourceRef.Id} at expected path: {filePath}");
            return null;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var midiFile = MidiFileParser.Parse(stream);
            return new MidiSequence(midiFile);
        }
        catch (Exception ex)
        {
            Log.Error($"Error loading MIDI data for source ID {sourceRef.Id}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Attempts to relink a missing audio source reference by providing a new file path.
    /// If successful, the reference is updated, and a new ISoundDataProvider is resolved.
    /// The caller is responsible for updating any AudioSegments in the composition that use this source reference.
    /// </summary>
    /// <param name="engine">The audio engine instance for context.</param>
    /// <param name="missingSourceReference">The ProjectSourceReference that is currently marked as missing.</param>
    /// <param name="newFilePath">The new absolute file path to the audio source.</param>
    /// <param name="projectDirectory">The base directory of the current project (used for resolving paths).</param>
    /// <returns>
    /// True if relinking was successful and a new ISoundDataProvider was resolved for the reference;
    /// otherwise, false. The updated ISoundDataProvider is set on missingSourceReference.ResolvedDataProvider.
    /// </returns>
    public static bool RelinkMissingMedia(
        AudioEngine engine,
        ProjectSourceReference missingSourceReference,
        string newFilePath,
        string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(missingSourceReference);
        ArgumentException.ThrowIfNullOrEmpty(newFilePath);
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);

        if (!File.Exists(newFilePath))
        {
            Log.Warning($"Relink failed: File not found at '{newFilePath}'.");
            return false;
        }

        // Update the source reference with the new path information
        missingSourceReference.OriginalAbsolutePath = newFilePath;
        missingSourceReference.ConsolidatedRelativePath =
            null; // It's no longer pointing to a consolidated version (if it was)
        missingSourceReference.IsEmbedded = false;
        missingSourceReference.EmbeddedDataB64 = null;
        missingSourceReference.IsMissing = true;

        // Try to resolve the new path into a data provider
        var newProvider = ResolveSourceReference(engine, missingSourceReference, projectDirectory);

        if (newProvider != null)
        {
            missingSourceReference.ResolvedDataProvider = newProvider;
            missingSourceReference.IsMissing = false;
            Log.Info($"Successfully relinked source ID {missingSourceReference.Id} to '{newFilePath}'.");
            return true;
        }

        Log.Warning($"Relink failed: Could not resolve data provider for '{newFilePath}'.");
        return false;
    }

    #endregion

    // Helper method to serialize modifiers/analyzers
    private static List<ProjectEffectData> SerializeEffects<T>(IEnumerable<T?> effects, JsonSerializerOptions jsonOptions) where T : class
    {
        var effectDataList = new List<ProjectEffectData>();
        foreach (var effect in effects)
        {
            if (effect is null) continue;
            var effectType = effect.GetType();
            var parameters = new JsonObject();

            foreach (var prop in effectType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop is not { CanRead: true, CanWrite: true } || prop.GetIndexParameters().Length != 0) continue;
                if (prop.DeclaringType == typeof(SoundComponent) ||
                    prop.DeclaringType == typeof(SoundModifier) ||
                    prop.DeclaringType == typeof(MidiModifier) ||
                    (prop.DeclaringType == typeof(AudioAnalyzer) && prop.Name is "Name" or "Enabled" or "Engine"))
                {
                    continue; // Skip base class properties that are handled separately or shouldn't be serialized
                }

                if (prop.Name is "ParentSegment" or "ParentTrack" or "ParentComposition") continue;


                try
                {
                    var value = prop.GetValue(effect);
                    if (value != null)
                    {
                        var propTypeInfo = jsonOptions.GetTypeInfo(prop.PropertyType);
                        parameters[prop.Name] = JsonValue.Create(JsonSerializer.SerializeToElement(value, propTypeInfo));
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        $"Could not serialize property '{prop.Name}' for effect type '{effectType.Name}': {ex.Message}");
                }
            }

            effectDataList.Add(new ProjectEffectData
            {
                TypeName = effectType.FullName ?? string.Empty,
                IsEnabled = effect switch
                {
                    SoundModifier sm => sm.Enabled,
                    AudioAnalyzer aa => aa.Enabled,
                    MidiModifier mm => mm.IsEnabled,
                    _ => false
                },
                Parameters = JsonDocument.Parse(parameters.ToJsonString(jsonOptions))
            });
        }

        return effectDataList;
    }


    // Helper method to deserialize modifiers/analyzers
    private static List<T> DeserializeEffects<T>(AudioFormat format, List<ProjectEffectData> effectDataList,
        Composition composition, JsonSerializerOptions jsonOptions) where T : class
    {
        var targetEffectList = new List<T>();
        foreach (var effectData in effectDataList)
        {
            if (string.IsNullOrEmpty(effectData.TypeName))
            {
                Log.Warning("Effect data found with no TypeName. Skipping.");
                continue;
            }

            var effectType = TypeRegistry.ResolveType(effectData.TypeName);
            if (effectType == null)
            {
                Log.Warning($"Could not find effect type '{effectData.TypeName}'. Effect will be skipped. Ensure it is registered in TypeRegistry.");
                continue;
            }

            if (!typeof(T).IsAssignableFrom(effectType))
            {
                Log.Warning(
                    $"Type '{effectData.TypeName}' is not assignable to target type '{typeof(T).Name}'. Skipping.");
                continue;
            }

            try
            {
                // Attempt to create instance, passing AudioFormat if constructor requires it.
                var constructorWithFormat = effectType.GetConstructor([typeof(AudioFormat)]);
                var effectInstance = constructorWithFormat != null
                    ? Activator.CreateInstance(effectType, format)
                    : Activator.CreateInstance(effectType);

                if (effectInstance is not T typedInstance)
                {
                    Log.Warning($"Could not create instance of effect type '{effectData.TypeName}'. Skipping.");
                    continue;
                }

                if (typedInstance is IMidiMappable mappable)
                {
                    composition.RegisterMappableObject(mappable);
                }

                switch (typedInstance)
                {
                    // Set IsEnabled for known types
                    case SoundModifier sm:
                        sm.Enabled = effectData.IsEnabled;
                        break;
                    case AudioAnalyzer aa:
                        aa.Enabled = effectData.IsEnabled;
                        break;
                    case MidiModifier mm:
                        mm.IsEnabled = effectData.IsEnabled;
                        break;
                }

                // Deserialize and set parameters
                if (effectData.Parameters != null)
                {
                    var parametersNode = effectData.Parameters.RootElement;
                    foreach (var propInfo in effectType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var propNameLower = char.ToLowerInvariant(propInfo.Name[0]) + propInfo.Name[1..];
                        if (propInfo.CanWrite && parametersNode.TryGetProperty(propNameLower, out var jsonProp))
                        {
                            try
                            {
                                var propTypeInfo = jsonOptions.GetTypeInfo(propInfo.PropertyType);
                                var value = jsonProp.Deserialize(propTypeInfo);
                                propInfo.SetValue(typedInstance, value);
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(
                                    $"Could not deserialize or set property '{propInfo.Name}' for effect '{effectType.Name}': {ex.Message}. Using default.");
                            }
                        }
                    }
                }

                targetEffectList.Add(typedInstance);
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"Error instantiating or setting parameters for effect '{effectData.TypeName}': {ex.Message}. Effect skipped.");
            }
        }

        return targetEffectList;
    }

    private static List<ProjectMidiMapping> SerializeMappings(IReadOnlyList<MidiMapping> mappings)
    {
        return mappings.Select(m => new ProjectMidiMapping
        {
            DeviceName = m.Source.DeviceName,
            Channel = m.Source.Channel,
            MessageType = m.Source.MessageType,
            MessageParameter = m.Source.MessageParameter,
            TargetObjectId = m.Target.TargetObjectId,
            TargetType = m.Target.TargetType,
            TargetMemberName = m.Target.TargetMemberName,
            Behavior = m.Behavior,
            Transformer = new ValueTransformer
            {
                SourceMin = m.Transformer.SourceMin,
                SourceMax = m.Transformer.SourceMax,
                TargetMin = m.Transformer.TargetMin,
                TargetMax = m.Transformer.TargetMax,
                CurveType = m.Transformer.CurveType
            }
        }).ToList();
    }

    private static void DeserializeMappings(List<ProjectMidiMapping> projectMappings, Composition composition)
    {
        foreach (var pm in projectMappings)
        {
            var mapping = new MidiMapping(
                new MidiInputSource
                {
                    DeviceName = pm.DeviceName,
                    Channel = pm.Channel,
                    MessageType = pm.MessageType,
                    MessageParameter = pm.MessageParameter
                },
                new MidiMappingTarget
                {
                    TargetObjectId = pm.TargetObjectId,
                    TargetType = pm.TargetType,
                    TargetMemberName = pm.TargetMemberName
                },
                new ValueTransformer
                {
                    SourceMin = pm.Transformer.SourceMin,
                    SourceMax = pm.Transformer.SourceMax,
                    TargetMin = pm.Transformer.TargetMin,
                    TargetMax = pm.Transformer.TargetMax,
                    CurveType = pm.Transformer.CurveType
                },
                pm.Behavior
            );

            // Validate the mapping upon loading
            if (composition.TryGetMappableObject(pm.TargetObjectId, out var targetObject) && targetObject != null)
            {
                var memberInfo = targetObject.GetType()
                    .GetMember(pm.TargetMemberName, BindingFlags.Public | BindingFlags.Instance).FirstOrDefault();
                if (memberInfo == null)
                {
                    mapping.IsResolved = false; // Member not found
                    Log.Warning(
                        $"Could not resolve member '{pm.TargetMemberName}' for target object ID '{pm.TargetObjectId}'. Mapping is broken.");
                }
            }
            else
            {
                mapping.IsResolved = false; // Object not found
                Log.Warning($"Could not find target object with ID '{pm.TargetObjectId}'. Mapping is broken.");
            }

            composition.MappingManager.AddMapping(mapping);
        }
    }
}