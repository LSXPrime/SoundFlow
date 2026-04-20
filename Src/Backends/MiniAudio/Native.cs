using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Enums;

namespace SoundFlow.Backends.MiniAudio;

internal static unsafe partial class Native
{
    
#if BROWSER
    private const string LibraryName = "libminiaudio";
#else
    private const string LibraryName = "miniaudio";
#endif

    #region Delegates

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AudioCallback(nint device, nint output, nint input, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate MiniAudioResult BufferProcessingCallback(
        nint pCodecContext, // The native decoder/encoder instance pointer (ma_decoder*, ma_encoder*)
        nint pBuffer, // The buffer pointer (void* pBufferOut or const void* pBufferIn)
        ulong bytesRequested, // The number of bytes requested (bytesToRead or bytesToWrite)
        out ulong bytesTransferred // The actual number of bytes processed/transferred (size_t*)
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate MiniAudioResult SeekCallback(nint pDecoder, long byteOffset, SeekPoint origin);

    #endregion

    #region Initialization

    static Native()
    {
        // Ignore Resolver in Browser since it throws "MONO_WASM: Exception marshalling result of JS promise to CS"
        if (!OperatingSystem.IsBrowser())
            NativeLibrary.SetDllImportResolver(typeof(MiniAudioEngine).Assembly, NativeLibraryResolver.Resolve);
    }

    private static class NativeLibraryResolver
    {
        [UnsupportedOSPlatform("browser")]
        public static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            // 1. Get the platform-specific library file name (e.g., "libminiaudio.so", "miniaudio.dll").
            var platformSpecificName = GetPlatformSpecificLibraryName(libraryName);

            // 2. Try to load the library using its platform-specific name, allowing OS to find it in standard paths.
            if (NativeLibrary.TryLoad(platformSpecificName, assembly, searchPath, out var library))
                return library;

            // 3. If that fails, try to load it from the application's 'runtimes' directory for self-contained apps.
            var relativePath = GetLibraryPath(libraryName); // This still gives the full relative path
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

            if (File.Exists(fullPath) && NativeLibrary.TryLoad(fullPath, out library))
                return library;

            Console.WriteLine($"Loading ${platformSpecificName} from path: {relativePath} || Full path {fullPath}");

            // 4. If not found, use Load() to let the runtime throw a detailed DllNotFoundException.
            return NativeLibrary.Load(fullPath);
        }

        /// <summary>
        /// Gets the platform-specific library name
        /// </summary>
        private static string GetPlatformSpecificLibraryName(string libraryName)
        {
            if (OperatingSystem.IsWindows())
                return $"{libraryName}.dll";

            if (OperatingSystem.IsMacOS())
                return $"lib{libraryName}.dylib";

            // For iOS frameworks, the binary has the same name as the framework
            if (OperatingSystem.IsIOS())
                return libraryName;

            if (OperatingSystem.IsBrowser())
                return $"lib{libraryName}.a";

            // Default to Linux/Android/FreeBSD convention
            return $"lib{libraryName}.so";
        }

        /// <summary>
        /// Constructs the relative path to the native library within the 'runtimes' folder.
        /// </summary>
        private static string GetLibraryPath(string libraryName)
        {
            const string relativeBase = "runtimes";
            var platformSpecificName = GetPlatformSpecificLibraryName(libraryName);

            string rid;
            if (OperatingSystem.IsWindows())
            {
                rid = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X86 => "win-x86",
                    Architecture.X64 => "win-x64",
                    Architecture.Arm64 => "win-arm64",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Windows architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }
            else if (OperatingSystem.IsMacOS())
            {
                rid = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "osx-x64",
                    Architecture.Arm64 => "osx-arm64",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported macOS architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }
            else if (OperatingSystem.IsLinux())
            {
                rid = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "linux-x64",
                    Architecture.Arm => "linux-arm",
                    Architecture.Arm64 => "linux-arm64",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Linux architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }
            else if (OperatingSystem.IsAndroid())
            {
                rid = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "android-x64",
                    Architecture.Arm => "android-arm",
                    Architecture.Arm64 => "android-arm64",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Android architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }
            else if (OperatingSystem.IsIOS())
            {
                rid = RuntimeInformation.ProcessArchitecture switch
                {
                    // iOS uses .framework folders
                    Architecture.Arm64 => "ios-arm64",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported iOS architecture: {RuntimeInformation.ProcessArchitecture}")
                };
                return Path.Combine(relativeBase, rid, "native", $"{libraryName}.framework", platformSpecificName);
            }
            else if (OperatingSystem.IsFreeBSD())
            {
                rid = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "freebsd-x64",
                    Architecture.Arm64 => "freebsd-arm64",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported FreeBSD architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }
            else if (OperatingSystem.IsBrowser())
            {
                return Path.Combine(relativeBase, "browser-wasm", "native", platformSpecificName);
            }
            else
            {
                throw new PlatformNotSupportedException(
                    $"Unsupported operating system: {RuntimeInformation.OSDescription}");
            }

            return Path.Combine(relativeBase, rid, "native", platformSpecificName);
        }
    }

    #endregion

    #region Encoder

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_encoder_init")]
    public static extern MiniAudioResult EncoderInit(BufferProcessingCallback onRead, SeekCallback onSeekCallback,
        nint pUserData, nint pConfig, nint pEncoder);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_encoder_init", StringMarshalling = StringMarshalling.Utf8)]
    public static partial MiniAudioResult EncoderInit(BufferProcessingCallback onRead, SeekCallback onSeekCallback,
        nint pUserData, nint pConfig, nint pEncoder);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_encoder_uninit")]
    public static extern void EncoderUninit(nint pEncoder);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_encoder_uninit")]
    public static partial void EncoderUninit(nint pEncoder);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_encoder_write_pcm_frames")]
    public static extern MiniAudioResult EncoderWritePcmFrames(nint pEncoder, nint pFramesIn, ulong frameCount,
        out ulong pFramesWritten);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_encoder_write_pcm_frames")]
    public static partial MiniAudioResult EncoderWritePcmFrames(nint pEncoder, nint pFramesIn, ulong frameCount,
        out ulong pFramesWritten);
#endif

    #endregion

    #region Decoder

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_decoder_init")]
    public static extern MiniAudioResult DecoderInit(BufferProcessingCallback onRead, SeekCallback onSeekCallback,
        nint pUserData,
        nint pConfig, nint pDecoder);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_init")]
    public static partial MiniAudioResult DecoderInit(BufferProcessingCallback onRead, SeekCallback onSeekCallback,
        nint pUserData,
        nint pConfig, nint pDecoder);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_decoder_uninit")]
    public static extern MiniAudioResult DecoderUninit(nint pDecoder);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_uninit")]
    public static partial MiniAudioResult DecoderUninit(nint pDecoder);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_decoder_read_pcm_frames")]
    public static extern MiniAudioResult DecoderReadPcmFrames(nint decoder, nint framesOut, uint frameCount,
        out ulong framesRead);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_read_pcm_frames")]
    public static partial MiniAudioResult DecoderReadPcmFrames(nint decoder, nint framesOut, uint frameCount,
        out ulong framesRead);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_decoder_seek_to_pcm_frame")]
    public static extern MiniAudioResult DecoderSeekToPcmFrame(nint decoder, ulong frame);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_seek_to_pcm_frame")]
    public static partial MiniAudioResult DecoderSeekToPcmFrame(nint decoder, ulong frame);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_decoder_get_length_in_pcm_frames")]
    public static extern MiniAudioResult DecoderGetLengthInPcmFrames(nint decoder, out ulong length);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_get_length_in_pcm_frames")]
    public static partial MiniAudioResult DecoderGetLengthInPcmFrames(nint decoder, out ulong length);
#endif

    #endregion

    #region Context

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_context_init")]
    public static extern MiniAudioResult ContextInit(nint backends, uint backendCount, nint config, nint context);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_context_init")]
    public static partial MiniAudioResult ContextInit(nint backends, uint backendCount, nint config, nint context);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_context_uninit")]
    public static extern void ContextUninit(nint context);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_context_uninit")]
    public static partial void ContextUninit(nint context);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_context_get_backend")]
    public static extern MiniAudioBackend ContextGetBackend(nint context);
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_context_get_backend")]
    public static partial MiniAudioBackend ContextGetBackend(nint context);
#endif

    #endregion

    #region Device

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_get_devices")]
    public static extern MiniAudioResult GetDevices(nint context, out nint pPlaybackDevices, out nint pCaptureDevices,
        out uint playbackDeviceCount, out uint captureDeviceCount);
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_get_devices")]
    public static partial MiniAudioResult GetDevices(nint context, out nint pPlaybackDevices, out nint pCaptureDevices,
        out uint playbackDeviceCount, out uint captureDeviceCount);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_device_init")]
    public static extern MiniAudioResult DeviceInit(nint context, nint config, nint device);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_device_init")]
    public static partial MiniAudioResult DeviceInit(nint context, nint config, nint device);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_device_uninit")]
    public static extern void DeviceUninit(nint device);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_device_uninit")]
    public static partial void DeviceUninit(nint device);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_device_start")]
    public static extern MiniAudioResult DeviceStart(nint device);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_device_start")]
    public static partial MiniAudioResult DeviceStart(nint device);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "ma_device_stop")]
    public static extern MiniAudioResult DeviceStop(nint device);
#else
    [LibraryImport(LibraryName, EntryPoint = "ma_device_stop")]
    public static partial MiniAudioResult DeviceStop(nint device);
#endif

    #endregion

    #region Allocations

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_allocate_encoder")]
    public static extern nint AllocateEncoder();
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_encoder")]
    public static partial nint AllocateEncoder();
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_allocate_decoder")]
    public static extern nint AllocateDecoder();
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_decoder")]
    public static partial nint AllocateDecoder();
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_allocate_context")]
    public static extern nint AllocateContext();
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_context")]
    public static partial nint AllocateContext();
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_allocate_device")]
    public static extern nint AllocateDevice();
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_device")]
    public static partial nint AllocateDevice();
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_allocate_decoder_config")]
    public static extern nint AllocateDecoderConfig(SampleFormat format, uint channels, uint sampleRate);
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_decoder_config")]
    public static partial nint AllocateDecoderConfig(SampleFormat format, uint channels, uint sampleRate);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_allocate_encoder_config")]
    public static extern nint AllocateEncoderConfig(SampleFormat format, uint channels,
        uint sampleRate);
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_encoder_config")]
    public static partial nint AllocateEncoderConfig(SampleFormat format, uint channels,
        uint sampleRate);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_allocate_device_config")]
    public static extern nint AllocateDeviceConfig(Capability capabilityType, uint sampleRate,
        IntPtr dataCallback, nint pSfConfig);
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_device_config")]
    public static partial nint AllocateDeviceConfig(Capability capabilityType, uint sampleRate,
        AudioCallback dataCallback, nint pSfConfig);
#endif

    #endregion

    #region Utils

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_free")]
    public static extern void Free(nint ptr);
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_free")]
    public static partial void Free(nint ptr);
#endif

#if BROWSER
    [DllImport(LibraryName, EntryPoint = "sf_free_device_infos")]
    public static extern void FreeDeviceInfos(nint deviceInfos, uint count);
#else
    [LibraryImport(LibraryName, EntryPoint = "sf_free_device_infos")]
    public static partial void FreeDeviceInfos(nint deviceInfos, uint count);
#endif

    #endregion
}