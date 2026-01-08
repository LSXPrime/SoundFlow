using System.Diagnostics.CodeAnalysis;
using SoundFlow.Components;
using SoundFlow.Midi.Modifier;
using SoundFlow.Modifiers;
using SoundFlow.Security.Analyzers;
using SoundFlow.Security.Modifiers;
using SoundFlow.Visualization;

namespace SoundFlow.Utils;

/// <summary>
/// A registry for resolving types by name at runtime in a NativeAOT-compatible way.
/// This prevents the linker from trimming required types and avoids the use of unsafe reflection.
/// </summary>
public static class TypeRegistry
{
    private static readonly Dictionary<string, Type> Registry = new()
    {
        // Sound Modifiers
        { typeof(AlgorithmicReverbModifier).FullName!, typeof(AlgorithmicReverbModifier) },
        { typeof(BassBoosterModifier).FullName!, typeof(BassBoosterModifier) },
        { typeof(ChorusModifier).FullName!, typeof(ChorusModifier) },
        { typeof(CompressorModifier).FullName!, typeof(CompressorModifier) },
        { typeof(DelayModifier).FullName!, typeof(DelayModifier) },
        { typeof(Filter).FullName!, typeof(Filter) },
        { typeof(FrequencyBandModifier).FullName!, typeof(FrequencyBandModifier) },
        { typeof(HighPassModifier).FullName!, typeof(HighPassModifier) },
        { typeof(LowPassModifier).FullName!, typeof(LowPassModifier) },
        { typeof(MultiChannelChorusModifier).FullName!, typeof(MultiChannelChorusModifier) },
        { typeof(ParametricEqualizer).FullName!, typeof(ParametricEqualizer) },
        { typeof(ResamplerModifier).FullName!, typeof(ResamplerModifier) },
        { typeof(TrebleBoosterModifier).FullName!, typeof(TrebleBoosterModifier) },
        { typeof(VocalExtractorModifier).FullName!, typeof(VocalExtractorModifier) },
        
        // Security Modifiers
        { typeof(OwnershipWatermarkEmbedModifier).FullName!, typeof(OwnershipWatermarkEmbedModifier) },
        { typeof(IntegrityWatermarkEmbedModifier).FullName!, typeof(IntegrityWatermarkEmbedModifier) },
        { typeof(StreamEncryptionModifier).FullName!, typeof(StreamEncryptionModifier) },

        // Audio Analyzers
        { typeof(LevelMeterAnalyzer).FullName!, typeof(LevelMeterAnalyzer) },
        { typeof(SpectrumAnalyzer).FullName!, typeof(SpectrumAnalyzer) },
        { typeof(VoiceActivityDetector).FullName!, typeof(VoiceActivityDetector) },
        { typeof(ContentFingerprintAnalyzer).FullName!, typeof(ContentFingerprintAnalyzer) },
        
        // Security Analyzers
        { typeof(OwnershipWatermarkExtractAnalyzer).FullName!, typeof(OwnershipWatermarkExtractAnalyzer) },
        { typeof(IntegrityWatermarkVerifyAnalyzer).FullName!, typeof(IntegrityWatermarkVerifyAnalyzer) },

        // MIDI Modifiers
        { typeof(ArpeggiatorModifier).FullName!, typeof(ArpeggiatorModifier) },
        { typeof(ChannelFilterModifier).FullName!, typeof(ChannelFilterModifier) },
        { typeof(HarmonizerModifier).FullName!, typeof(HarmonizerModifier) },
        { typeof(RandomizerModifier).FullName!, typeof(RandomizerModifier) },
        { typeof(TransposeModifier).FullName!, typeof(TransposeModifier) },
        { typeof(VelocityModifier).FullName!, typeof(VelocityModifier) }
    };

    /// <summary>
    /// Registers a custom type to ensure it can be resolved during project loading.
    /// This method must be called for any user-defined Modifiers or Analyzers when running in NativeAOT.
    /// </summary>
    /// <typeparam name="T">The type to register. Must have public properties and constructors.</typeparam>
    public static void RegisterType<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
    {
        var type = typeof(T);
        if (type.FullName != null)
        {
            Registry[type.FullName] = type;
        }
    }

    /// <summary>
    /// Resolves a type by its full name from the registry.
    /// </summary>
    /// <param name="typeName">The full name of the type.</param>
    /// <returns>The resolved <see cref="Type"/> or null if not found.</returns>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
    public static Type? ResolveType(string typeName)
    {
        if (Registry.TryGetValue(typeName, out var type))
            return type;

        // Fallback for JIT environments (non-AOT) where Type.GetType might still work for unregistered types.
#pragma warning disable IL2057
#pragma warning disable IL2026 // Suppress warning about requires unreferenced code
        return Type.GetType(typeName);
#pragma warning restore IL2026
#pragma warning restore IL2057
    }
}