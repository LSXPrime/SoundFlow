using System.Text.Json;
using SoundFlow.Interfaces;
using SoundFlow.Providers;
using SoundFlow.Enums;
using SoundFlow.Abstracts;
using System.Buffers;
using System.Reflection;
using System.Text.Json.Nodes;
using SoundFlow.Structs;
using SoundFlow.Utils;

namespace SoundFlow.Editing.Persistence;

/// <summary>
/// Manages the saving and loading of audio composition projects.
/// </summary>
public static class CompositionProjectManager
{
    private const string CurrentProjectFileVersion = "1.2.0";
    private const string ConsolidatedMediaFolderName = "Assets";
    private const long MaxEmbedSizeBytes = 1 * 1024 * 1024; // 1MB threshold for embedding

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    
    #region Saving

    /// <summary>
    /// Saves the given composition to the specified project file path.
    /// </summary>
    /// <param name="engine">The audio engine instance of the composition.</param>
    /// <param name="composition">The composition to save.</param>
    /// <param name="projectFilePath">The full path where the project file will be saved.</param>
    /// <param name="consolidateMedia">If true, external and in-memory/stream audio sources will be processed for consolidation.</param>
    /// <param name="embedSmallMedia">If true, small audio sources (below a threshold) will be embedded directly into the project file.</param>
    public static async Task SaveProjectAsync(
        AudioEngine engine,
        Composition composition,
        string projectFilePath,
        bool consolidateMedia = true,
        bool embedSmallMedia = true)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrEmpty(projectFilePath);

        var projectData = new ProjectData
        {
            ProjectFileVersion = CurrentProjectFileVersion,
            Name = composition.Name,
            MasterVolume = composition.MasterVolume,
            TargetSampleRate = composition.SampleRate,
            TargetChannels = composition.TargetChannels,
            Modifiers = SerializeEffects(composition.Modifiers),
            Analyzers = SerializeEffects(composition.Analyzers)
        };

        var projectDirectory = Path.GetDirectoryName(projectFilePath)
                               ?? throw new IOException("Invalid project file path.");
        var mediaAssetsDirectory = Path.Combine(projectDirectory, ConsolidatedMediaFolderName);

        if (consolidateMedia) Directory.CreateDirectory(mediaAssetsDirectory);

        var sourceProviderMap = new Dictionary<ISoundDataProvider, ProjectSourceReference>();

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
                    Modifiers = SerializeEffects(track.Settings.Modifiers),
                    Analyzers = SerializeEffects(track.Settings.Analyzers)
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
                        consolidateMedia,
                        embedSmallMedia,
                        composition.SampleRate,
                        composition.TargetChannels
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
                    Settings =  new ProjectAudioSegmentSettings
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
                        Modifiers = SerializeEffects(segment.Settings.Modifiers),
                        Analyzers = SerializeEffects(segment.Settings.Analyzers)
                    }
                });
            }
            projectData.Tracks.Add(projectTrack);
        }

        var json = JsonSerializer.Serialize(projectData, SerializerOptions);
        await File.WriteAllTextAsync(projectFilePath, json);

        composition.ClearDirtyFlag();
    }

    private static async Task<ProjectSourceReference> CreateSourceReferenceAsync(
        ISoundDataProvider provider,
        AudioEngine engine,
        string projectDirectory,
        string mediaAssetsDirectory,
        bool consolidateMedia,
        bool embedSmallMedia,
        int compositionSampleRate,
        int compositionTargetChannels)
    {
        var sourceRef = new ProjectSourceReference
        {
            OriginalSampleFormat = provider.SampleFormat,
            OriginalSampleRate = provider.SampleRate
        };
        
        // 1. Attempt Embedding (if enabled and suitable)
        // Provider must be seekable and have a known, finite length for embedding.
        if (embedSmallMedia && provider is { CanSeek: true, Length: > 0 } &&
            (long)provider.Length * provider.SampleFormat.GetBytesPerSample() < MaxEmbedSizeBytes)
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
        if (consolidateMedia && provider is { CanSeek: true, Length: > 0 })
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
                        // ISoundDataProvider doesn't expose its native channel count. This is a limitation.
                        // For now, I will use the composition's target channels for the encoded WAV.
                        // This means mono sources might become stereo, or vice versa, if compositionTargetChannels differs.
                        // TODO: refactor when support for getting audio data is added (e.g., mono, stereo, 5.1 or 7.1, etc.)
                        var audioFormat = new AudioFormat
                        {
                            Format = SampleFormat.F32,
                            Channels = compositionTargetChannels,
                            SampleRate = provider.SampleRate
                        };
                        var stream = new FileStream(consolidatedFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096);
                        var encoder = engine.CreateEncoder(stream, EncodingFormat.Wav, audioFormat);
                        encoder.Encode(tempBuffer.AsSpan(0, samplesRead));
                        encoder.Dispose();
                        await stream.DisposeAsync();

                        sourceRef.OriginalSampleFormat = audioFormat.Format;
                        sourceRef.OriginalSampleRate = audioFormat.SampleRate;
                    }
                    else if (totalSamples == 0)
                    {
                        // Handle empty provider, create an empty WAV file or skip consolidation for it.
                        await File.WriteAllBytesAsync(consolidatedFilePath, CreateEmptyWavHeader(compositionSampleRate, compositionTargetChannels, SampleFormat.F32));
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not read all samples from in-memory provider for consolidation (ID: {sourceRef.Id}). Expected {totalSamples}, got {samplesRead}.");
                        return sourceRef; // Return without consolidated path
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(tempBuffer);
                }
            }
            sourceRef.ConsolidatedRelativePath = Path.GetRelativePath(projectDirectory, consolidatedFilePath).Replace(Path.DirectorySeparatorChar, '/');
            return sourceRef;
        }

        // 3. If not embedded and not consolidated (or consolidation failed for in-memory), return a placeholder.
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
        writer.Write((short) (format == SampleFormat.F32 ? 3 : 1)); // AudioFormat (3 for IEEE float, 1 for PCM)
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
    /// <returns>A tuple containing the loaded Composition and a list of missing/unresolved source references.</returns>
    public static async Task<(Composition Composition, List<ProjectSourceReference> UnresolvedSources)> LoadProjectAsync(AudioEngine engine, AudioFormat format, string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
            throw new FileNotFoundException("Project file not found.", projectFilePath);

        var json = await File.ReadAllTextAsync(projectFilePath);
        var projectData = JsonSerializer.Deserialize<ProjectData>(json, SerializerOptions)
                          ?? throw new JsonException("Failed to deserialize project data.");

        if (Version.TryParse(projectData.ProjectFileVersion, out var fileVersion) &&
            Version.TryParse(CurrentProjectFileVersion, out var currentVersion) &&
            fileVersion.Major > currentVersion.Major)
        {
            Console.WriteLine($"Warning: Loading project file version {projectData.ProjectFileVersion} " +
                              $"with library version {CurrentProjectFileVersion}. Forward compatibility is not guaranteed.");
        }

        var composition = new Composition(format, projectData.Name)
        {
            MasterVolume = projectData.MasterVolume,
            SampleRate = projectData.TargetSampleRate,
            TargetChannels = projectData.TargetChannels,
        };
        composition.Modifiers.AddRange(DeserializeEffects<SoundModifier>(format, projectData.Modifiers));
        composition.Analyzers.AddRange(DeserializeEffects<AudioAnalyzer>(format, projectData.Analyzers));

        var projectDirectory = Path.GetDirectoryName(projectFilePath)
                               ?? throw new IOException("Invalid project file path.");
        var unresolvedSources = new List<ProjectSourceReference>();

        var resolvedProviderCache = new Dictionary<Guid, ISoundDataProvider>();

        foreach (var sourceRef in projectData.SourceReferences)
        {
            if (!resolvedProviderCache.TryGetValue(sourceRef.Id, out var value))
            {
                sourceRef.ResolvedDataProvider = ResolveSourceReferenceAsync(engine, format, sourceRef, projectDirectory);
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
            trackSettings.Modifiers.AddRange(DeserializeEffects<SoundModifier>(format, projectTrack.Settings.Modifiers));
            trackSettings.Analyzers.AddRange(DeserializeEffects<AudioAnalyzer>(format, projectTrack.Settings.Analyzers));

            var track = new Track(projectTrack.Name, trackSettings)
            {
                ParentComposition = composition
            };

            foreach (var projectSegment in projectTrack.Segments)
            {
                var sourceRef = projectData.SourceReferences.FirstOrDefault(sr => sr.Id == projectSegment.SourceReferenceId);
                var providerToUse = sourceRef?.ResolvedDataProvider;

                if (providerToUse == null)
                {
                    Console.WriteLine($"Warning: Audio source for segment '{projectSegment.Name}' (Ref ID: {projectSegment.SourceReferenceId}) is missing. Using placeholder.");
                    var silentDuration = projectSegment.SourceDuration;
                    var placeholderSampleCount = (int)(Math.Max(silentDuration.TotalSeconds, 0.1) * composition.SampleRate * composition.TargetChannels);
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
                segmentSettings.Modifiers.AddRange(DeserializeEffects<SoundModifier>(format, projectSegment.Settings.Modifiers));
                segmentSettings.Analyzers.AddRange(DeserializeEffects<AudioAnalyzer>(format, projectSegment.Settings.Analyzers));

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
                // ParentSegment is set implicitly by AudioSegment constructor, so we can set stretch properties here.
                if (projectSegment.Settings.TargetStretchDuration.HasValue)
                    segment.Settings.TargetStretchDuration = projectSegment.Settings.TargetStretchDuration;
                else
                    segment.Settings.TimeStretchFactor = projectSegment.Settings.TimeStretchFactor;

                track.AddSegment(segment);
            }
            composition.AddTrack(track);
        }

        composition.ClearDirtyFlag();
        return (composition, unresolvedSources);
    }

    private static ISoundDataProvider? ResolveSourceReferenceAsync(AudioEngine engine, AudioFormat format, ProjectSourceReference sourceRef, string projectDirectory)
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
                Console.WriteLine($"Warning: Unsupported embedded format {sourceRef.OriginalSampleFormat} for source ID {sourceRef.Id}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error decoding embedded data for source ID {sourceRef.Id}: {ex.Message}");
                return null;
            }
        }

        string? pathToTry;

        // 2. Try Consolidated Relative Path
        if (!string.IsNullOrEmpty(sourceRef.ConsolidatedRelativePath))
        {
            pathToTry = Path.GetFullPath(Path.Combine(projectDirectory, sourceRef.ConsolidatedRelativePath));
            if (File.Exists(pathToTry))
                return new StreamDataProvider(engine, format, new FileStream(pathToTry, FileMode.Open, FileAccess.Read, FileShare.Read));
            
            Console.WriteLine($"Warning: Consolidated file not found for source ID {sourceRef.Id} at expected path: {pathToTry}");
        }

        // 3. Try Original Absolute Path
        if (!string.IsNullOrEmpty(sourceRef.OriginalAbsolutePath))
        {
            pathToTry = sourceRef.OriginalAbsolutePath;
             if (File.Exists(pathToTry))
                 return new StreamDataProvider(engine, format, new FileStream(pathToTry, FileMode.Open, FileAccess.Read, FileShare.Read));
             
             Console.WriteLine($"Warning: Original absolute file not found for source ID {sourceRef.Id} at path: {pathToTry}");
        }

        // If all attempts fail
        return null;
    }

    /// <summary>
    /// Attempts to relink a missing audio source reference by providing a new file path.
    /// If successful, the reference is updated, and a new ISoundDataProvider is resolved.
    /// The caller is responsible for updating any AudioSegments in the composition that use this source reference.
    /// </summary>
    /// <param name="engine">The audio engine instance for context.</param>
    /// <param name="format">The audio format of the composition.</param>
    /// <param name="missingSourceReference">The ProjectSourceReference that is currently marked as missing.</param>
    /// <param name="newFilePath">The new absolute file path to the audio source.</param>
    /// <param name="projectDirectory">The base directory of the current project (used for resolving paths).</param>
    /// <returns>
    /// True if relinking was successful and a new ISoundDataProvider was resolved for the reference;
    /// otherwise, false. The updated ISoundDataProvider is set on missingSourceReference.ResolvedDataProvider.
    /// </returns>
    public static bool RelinkMissingMediaAsync(
        AudioEngine engine,
        AudioFormat format,
        ProjectSourceReference missingSourceReference,
        string newFilePath,
        string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(missingSourceReference);
        ArgumentException.ThrowIfNullOrEmpty(newFilePath);
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);

        if (!File.Exists(newFilePath))
        {
            Console.WriteLine($"Relink failed: File not found at '{newFilePath}'.");
            return false;
        }

        // Update the source reference with the new path information
        missingSourceReference.OriginalAbsolutePath = newFilePath;
        missingSourceReference.ConsolidatedRelativePath = null; // It's no longer pointing to a consolidated version (if it was)
        missingSourceReference.IsEmbedded = false;
        missingSourceReference.EmbeddedDataB64 = null;
        missingSourceReference.IsMissing = true;

        // Try to resolve the new path into a data provider
        var newProvider = ResolveSourceReferenceAsync(engine, format, missingSourceReference, projectDirectory);

        if (newProvider != null)
        {
            missingSourceReference.ResolvedDataProvider = newProvider;
            missingSourceReference.IsMissing = false;
            Console.WriteLine($"Successfully relinked source ID {missingSourceReference.Id} to '{newFilePath}'.");
            return true;
        }

        Console.WriteLine($"Relink failed: Could not resolve data provider for '{newFilePath}'.");
        return false;
    }


    #endregion
    
    // Helper method to serialize modifiers/analyzers
    private static List<ProjectEffectData> SerializeEffects<T>(IEnumerable<T> effects) where T : class
    {
        var effectDataList = new List<ProjectEffectData>();
        foreach (var effect in effects)
        {
            var effectType = effect.GetType();
            var parameters = new JsonObject();
            
            // Serialize public, settable properties
            foreach (var prop in effectType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop is not { CanRead: true, CanWrite: true } || prop.GetIndexParameters().Length != 0) continue;
                if (prop.DeclaringType == typeof(SoundComponent) || 
                    prop.DeclaringType == typeof(SoundModifier) ||
                    (prop.DeclaringType == typeof(AudioAnalyzer) && prop.Name is "Name" or "Enabled" or "Engine"))
                {
                    continue; // Skip base class properties that are handled separately or shouldn't be serialized
                }
                if (prop.Name is "ParentSegment" or "ParentTrack" or "ParentComposition") continue;


                try
                {
                    var value = prop.GetValue(effect);
                    if (value != null) parameters[prop.Name] = JsonValue.Create(JsonSerializer.SerializeToElement(value, SerializerOptions));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not serialize property '{prop.Name}' for effect type '{effectType.Name}': {ex.Message}");
                }
            }

            effectDataList.Add(new ProjectEffectData
            {
                TypeName = effectType.AssemblyQualifiedName ?? effectType.FullName ?? string.Empty,
                IsEnabled = effect switch
                {
                    SoundModifier sm => sm.Enabled,
                    AudioAnalyzer aa => aa.Enabled,
                    _ => false
                },
                Parameters = JsonDocument.Parse(parameters.ToJsonString(SerializerOptions))
            });
        }
        return effectDataList;
    }


    // Helper method to deserialize modifiers/analyzers
    private static List<T> DeserializeEffects<T>(AudioFormat format, List<ProjectEffectData> effectDataList) where T : class
    {
        var targetEffectList = new List<T>();
        foreach (var effectData in effectDataList)
        {
            if (string.IsNullOrEmpty(effectData.TypeName))
            {
                Console.WriteLine("Warning: Effect data found with no TypeName. Skipping.");
                continue;
            }

            var effectType = Type.GetType(effectData.TypeName);
            if (effectType == null)
            {
                Console.WriteLine($"Warning: Could not find effect type '{effectData.TypeName}'. Effect will be skipped.");
                continue;
            }
            
            if (!typeof(T).IsAssignableFrom(effectType))
            {
                Console.WriteLine($"Warning: Type '{effectData.TypeName}' is not assignable to target type '{typeof(T).Name}'. Skipping.");
                continue;
            }

            try
            {
                // Attempt to create instance, passing device if constructor requires it.
                var constructor = effectType.GetConstructor([typeof(AudioFormat)]);
                var effectInstance = constructor != null ? Activator.CreateInstance(effectType, format) : Activator.CreateInstance(effectType);

                if (effectInstance is not T typedInstance)
                {
                    Console.WriteLine($"Warning: Could not create instance of effect type '{effectData.TypeName}'. Skipping.");
                    continue;
                }

                switch (typedInstance)
                {
                    // Set IsEnabled for SoundModifiers
                    case SoundModifier sm:
                        sm.Enabled = effectData.IsEnabled;
                        break;
                    case AudioAnalyzer aa:
                        aa.Enabled = effectData.IsEnabled;
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
                                var value = JsonSerializer.Deserialize(jsonProp.GetRawText(), propInfo.PropertyType, SerializerOptions);
                                propInfo.SetValue(typedInstance, value);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not deserialize or set property '{propInfo.Name}' for effect '{effectType.Name}': {ex.Message}. Using default.");
                            }
                        }
                    }
                }
                targetEffectList.Add(typedInstance);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error instantiating or setting parameters for effect '{effectData.TypeName}': {ex.Message}. Effect skipped.");
            }
        }
        
        return targetEffectList;
    }
}
