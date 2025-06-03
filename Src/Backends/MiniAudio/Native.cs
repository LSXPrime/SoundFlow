using System.Reflection;
using System.Runtime.InteropServices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Enums;

namespace SoundFlow.Backends.MiniAudio;

internal static unsafe partial class Native
{
    private const string LibraryName = "miniaudio"; 
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AudioCallback(nint device, nint output, nint input, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Result BufferProcessingCallback(
        nint pCodecContext,          // The native decoder/encoder instance pointer (ma_decoder*, ma_encoder*)
        nint pBuffer,                // The buffer pointer (void* pBufferOut or const void* pBufferIn)
        ulong bytesRequested,        // The number of bytes requested (bytesToRead or bytesToWrite)
        out ulong* bytesTransferred   // The actual number of bytes processed/transferred (size_t*)
    );

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Result SeekCallback(nint pDecoder, long byteOffset, SeekPoint origin);
    
    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, NativeLibraryResolver.Resolve);
    }

    private static class NativeLibraryResolver
    {
        public static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (NativeLibrary.TryLoad(libraryName, out var library))
                return library;
            
            var libraryPath = GetLibraryPath(libraryName);
            
            // Safeguard against dotnet cli working directory inconsistency
            if (NativeLibrary.TryLoad(libraryName, out library))
                return library;
            
            libraryPath = $"{AppDomain.CurrentDomain.BaseDirectory}/{libraryPath}";
            return NativeLibrary.Load(libraryPath);
        }

        private static string GetLibraryPath(string libraryName)
        {
            const string relativeBase = "runtimes";
            if (OperatingSystem.IsWindows())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X86 => $"{relativeBase}/win-x86/native/{libraryName}.dll",
                    Architecture.X64 => $"{relativeBase}/win-x64/native/{libraryName}.dll",
                    Architecture.Arm64 => $"{relativeBase}/win-arm64/native/{libraryName}.dll",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Windows architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsMacOS())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => $"{relativeBase}/osx-x64/native/lib{libraryName}.dylib",
                    Architecture.Arm64 => $"{relativeBase}/osx-arm64/native/lib{libraryName}.dylib",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported macOS architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsLinux())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => $"{relativeBase}/linux-x64/native/lib{libraryName}.so",
                    Architecture.Arm => $"{relativeBase}/linux-arm/native/lib{libraryName}.so",
                    Architecture.Arm64 => $"{relativeBase}/linux-arm64/native/lib{libraryName}.so",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Linux architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsAndroid())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => $"{relativeBase}/android-x64/native/lib{libraryName}.so",
                    Architecture.Arm => $"{relativeBase}/android-arm/native/lib{libraryName}.so",
                    Architecture.Arm64 => $"{relativeBase}/android-arm64/native/lib{libraryName}.so",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Android architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsIOS())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => $"{relativeBase}/ios-arm64/native/{libraryName}.framework/{libraryName}",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported iOS architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsFreeBSD())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => $"{relativeBase}/freebsd-x64/native/lib{libraryName}.so",
                    Architecture.Arm64 => $"{relativeBase}/freebsd-arm64/native/lib{libraryName}.so",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported FreeBSD architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            throw new PlatformNotSupportedException(
                $"Unsupported operating system: {RuntimeInformation.OSDescription}");
        }
    }
    
    #region Encoder

    [LibraryImport(LibraryName, EntryPoint = "ma_encoder_init", StringMarshalling = StringMarshalling.Utf8)]
    public static partial Result EncoderInit(BufferProcessingCallback onRead, SeekCallback onSeekCallback, nint pUserData, nint pConfig, nint pEncoder);

    [LibraryImport(LibraryName, EntryPoint = "ma_encoder_uninit")]
    public static partial void EncoderUninit(nint pEncoder);

    [LibraryImport(LibraryName, EntryPoint = "ma_encoder_write_pcm_frames")]
    public static partial Result EncoderWritePcmFrames(nint pEncoder, nint pFramesIn, ulong frameCount,
        ulong* pFramesWritten);

    #endregion

    #region Decoder

    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_init")]
    public static partial Result DecoderInit(BufferProcessingCallback onRead, SeekCallback onSeekCallback, nint pUserData,
        nint pConfig, nint pDecoder);

    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_uninit")]
    public static partial Result DecoderUninit(nint pDecoder);

    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_read_pcm_frames")]
    public static partial Result DecoderReadPcmFrames(nint decoder, nint framesOut, uint frameCount,
        out ulong framesRead);

    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_seek_to_pcm_frame")]
    public static partial Result DecoderSeekToPcmFrame(nint decoder, ulong frame);

    [LibraryImport(LibraryName, EntryPoint = "ma_decoder_get_length_in_pcm_frames")]
    public static partial Result DecoderGetLengthInPcmFrames(nint decoder, out uint* length);

    #endregion

    #region Context

    [LibraryImport(LibraryName, EntryPoint = "ma_context_init")]
    public static partial Result ContextInit(nint backends, uint backendCount, nint config, nint context);
    
    [LibraryImport(LibraryName, EntryPoint = "ma_context_uninit")]
    public static partial void ContextUninit(nint context);

    #endregion

    #region Device

    [LibraryImport(LibraryName, EntryPoint = "sf_get_devices")]
    public static partial Result GetDevices(nint context, out nint pPlaybackDevices, out nint pCaptureDevices, out nint playbackDeviceCount, out nint captureDeviceCount);

    [LibraryImport(LibraryName, EntryPoint = "ma_device_init")]
    public static partial Result DeviceInit(nint context, nint config, nint device);

    [LibraryImport(LibraryName, EntryPoint = "ma_device_uninit")]
    public static partial void DeviceUninit(nint device);

    [LibraryImport(LibraryName, EntryPoint = "ma_device_start")]
    public static partial Result DeviceStart(nint device);

    [LibraryImport(LibraryName, EntryPoint = "ma_device_stop")]
    public static partial Result DeviceStop(nint device);

    #endregion

    #region Allocations

    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_encoder")]
    public static partial nint AllocateEncoder();

    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_decoder")]
    public static partial nint AllocateDecoder();

    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_context")]
    public static partial nint AllocateContext();

    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_device")]
    public static partial nint AllocateDevice();

    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_decoder_config")]
    public static partial nint AllocateDecoderConfig(SampleFormat format, uint channels, uint sampleRate);

    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_encoder_config")]
    public static partial nint AllocateEncoderConfig(EncodingFormat encodingFormat, SampleFormat format, uint channels,
        uint sampleRate);

    [LibraryImport(LibraryName, EntryPoint = "sf_allocate_device_config")]
    public static partial nint AllocateDeviceConfig(Capability capabilityType, SampleFormat format, uint channels,
        uint sampleRate, AudioCallback dataCallback, nint playbackDevice, nint captureDevice);

    #endregion

    #region Utils

    [LibraryImport(LibraryName, EntryPoint = "sf_free")]
    public static partial void Free(nint ptr);

    #endregion
}