#if !BROWSER
using System.Reflection;
using System.Runtime.InteropServices;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Enums;

namespace SoundFlow.Backends.MiniAudio;

public static unsafe partial class Native
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void AudioCallback(nint device, nint output, nint input, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Result DecoderRead(nint pDecoder, nint pBufferOut, ulong bytesToRead, nint pBytesRead);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate Result DecoderSeek(nint pDecoder, long byteOffset, SeekPoint origin);

    static Native()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), NativeLibraryResolver.Resolve);
    }

    private static class NativeLibraryResolver
    {
        public static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            var libraryPath = GetLibraryPath("libminiaudio");
            return NativeLibrary.Load(libraryPath);
        }


        private static string GetLibraryPath(string libraryName)
        {
            var relativeBase = Directory.Exists("runtimes") ? "runtimes" :
                Directory.EnumerateDirectories(Directory.GetCurrentDirectory(), "runtimes", SearchOption.AllDirectories)
                    .Select(dirPath => Path.GetRelativePath(Directory.GetCurrentDirectory(), dirPath))
                    .FirstOrDefault();

            if (string.IsNullOrEmpty(relativeBase))
                throw new DirectoryNotFoundException("Unable to find runtimes directory.");

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
                    Architecture.X64 => $"{relativeBase}/osx-x64/native/{libraryName}.dylib",
                    Architecture.Arm64 => $"{relativeBase}/osx-arm64/native/{libraryName}.dylib",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported macOS architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsLinux())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => $"{relativeBase}/linux-x64/native/{libraryName}.so",
                    Architecture.Arm => $"{relativeBase}/linux-arm/native/{libraryName}.so",
                    Architecture.Arm64 => $"{relativeBase}/linux-arm64/native/{libraryName}.so",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Linux architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsAndroid())
            {
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => $"{relativeBase}/android-x64/native/{libraryName}.so",
                    Architecture.Arm => $"{relativeBase}/android-arm/native/{libraryName}.so",
                    Architecture.Arm64 => $"{relativeBase}/android-arm64/native/{libraryName}.so",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported Android architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            if (OperatingSystem.IsIOS())
            {
                libraryName = libraryName.Replace("lib", "");
                return RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.Arm64 => $"{relativeBase}/ios-arm64/native/{libraryName}.framework/{libraryName}",
                    _ => throw new PlatformNotSupportedException(
                        $"Unsupported iOS architecture: {RuntimeInformation.ProcessArchitecture}")
                };
            }

            throw new PlatformNotSupportedException(
                $"Unsupported operating system: {RuntimeInformation.OSDescription}");
        }
    }

    #region Encoder

    [LibraryImport("libminiaudio", EntryPoint = "ma_encoder_init_file", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int EncoderInitFile(string filePath, nint pConfig, nint pEncoder);

    [LibraryImport("libminiaudio", EntryPoint = "ma_encoder_uninit")]
    public static partial void EncoderUninit(nint pEncoder);

    [LibraryImport("libminiaudio", EntryPoint = "ma_encoder_write_pcm_frames")]
    public static partial int EncoderWritePcmFrames(nint pEncoder, nint pFramesIn, int frameCount,
        nint pFramesWritten);

    #endregion

    #region Decoder

    [LibraryImport("libminiaudio", EntryPoint = "ma_decoder_init")]
    public static partial int DecoderInit(nint onRead, nint onSeek, nint pUserData,
        nint pConfig, nint pDecoder);

    [LibraryImport("libminiaudio", EntryPoint = "ma_decoder_uninit")]
    public static partial int DecoderUninit(nint pDecoder);

    [LibraryImport("libminiaudio", EntryPoint = "ma_decoder_read_pcm_frames")]
    public static partial int DecoderReadPcmFrames(nint decoder, nint framesOut, uint frameCount,
        nint framesRead);

    [LibraryImport("libminiaudio", EntryPoint = "ma_decoder_seek_to_pcm_frame")]
    public static partial int DecoderSeekToPcmFrame(nint decoder, int frame);

    [LibraryImport("libminiaudio", EntryPoint = "ma_decoder_get_length_in_pcm_frames")]
    public static partial int DecoderGetLengthInPcmFrames(nint decoder, nint length);

    #endregion

    #region Context

    [LibraryImport("libminiaudio", EntryPoint = "ma_context_init")]
    public static partial int ContextInit(nint backends, int backendCount, nint config, nint context);

    [LibraryImport("libminiaudio", EntryPoint = "ma_context_uninit")]
    public static partial void ContextUninit(nint context);

    #endregion

    #region Device

    [LibraryImport("libminiaudio", EntryPoint = "sf_get_devices")]
    public static partial int GetDevices(nint context, out nint pPlaybackDevices, out nint pCaptureDevices, out nint playbackDeviceCount, out nint captureDeviceCount);

    [LibraryImport("libminiaudio", EntryPoint = "ma_device_init")]
    public static partial int DeviceInit(nint context, nint config, nint device);

    [LibraryImport("libminiaudio", EntryPoint = "ma_device_uninit")]
    public static partial void DeviceUninit(nint device);

    [LibraryImport("libminiaudio", EntryPoint = "ma_device_start")]
    public static partial int DeviceStart(nint device);

    [LibraryImport("libminiaudio", EntryPoint = "ma_device_stop")]
    public static partial int DeviceStop(nint device);

    #endregion

    #region Allocations

    [LibraryImport("libminiaudio", EntryPoint = "sf_allocate_encoder")]
    public static partial nint AllocateEncoder();

    [LibraryImport("libminiaudio", EntryPoint = "sf_allocate_decoder")]
    public static partial nint AllocateDecoder();

    [LibraryImport("libminiaudio", EntryPoint = "sf_allocate_context")]
    public static partial nint AllocateContext();

    [LibraryImport("libminiaudio", EntryPoint = "sf_allocate_device")]
    public static partial nint AllocateDevice();

    [LibraryImport("libminiaudio", EntryPoint = "sf_allocate_decoder_config")]
    public static partial nint AllocateDecoderConfig(int format, int channels, int sampleRate);

    [LibraryImport("libminiaudio", EntryPoint = "sf_allocate_encoder_config")]
    public static partial nint AllocateEncoderConfig(int encodingFormat, int format, int channels, int sampleRate);

    [LibraryImport("libminiaudio", EntryPoint = "sf_allocate_device_config")]
    public static partial nint AllocateDeviceConfig(int capabilityType, int format, int channels,
        int sampleRate, nint dataCallback, nint playbackDevice, nint captureDevice);

    #endregion

    #region Utils

    [LibraryImport("libminiaudio", EntryPoint = "sf_free")]
    public static partial void Free(nint ptr);

    #endregion
}
#endif