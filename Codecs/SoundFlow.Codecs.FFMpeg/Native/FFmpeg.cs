using System.Reflection;
using SoundFlow.Enums;
using System.Runtime.InteropServices;
using SoundFlow.Codecs.FFMpeg.Enums;

namespace SoundFlow.Codecs.FFMpeg.Native;

/// <summary>
/// Defines the origin for seek operations, mirroring System.IO.SeekOrigin.
/// </summary>
internal enum SeekWhence
{
    /// <summary>
    /// Specifies the beginning of a stream.
    /// </summary>
    Set = 0,
    /// <summary>
    /// Specifies the current position within a stream.
    /// </summary>
    Cur = 1,
    /// <summary>
    /// Specifies the end of a stream.
    /// </summary>
    End = 2,
}

/// <summary>
/// Provides P/Invoke declarations for the native soundflow_ffmpeg library.
/// </summary>
internal static partial class FFmpeg
{
    private const string LibraryName = "soundflow-ffmpeg";

    #region Delegates
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate nuint ReadCallback(IntPtr pUserData, IntPtr pBuffer, nuint bytesToRead);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate long SeekCallback(IntPtr pUserData, long offset, SeekWhence whence);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate nuint WriteCallback(IntPtr pUserData, IntPtr pBuffer, nuint bytesToWrite);
    
    #endregion
    
        
    #region Initialization
    
    static FFmpeg()
    {
        NativeLibrary.SetDllImportResolver(typeof(FFmpeg).Assembly, NativeLibraryResolver.Resolve);
    }

    private static class NativeLibraryResolver
    {
        public static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            // 1. Get the platform-specific library file name (e.g., "libsoundflow-ffmpeg.so", "soundflow-ffmpeg.dll").
            var platformSpecificName = GetPlatformSpecificLibraryName(libraryName);

            // 2. Try to load the library using its platform-specific name, allowing OS to find it in standard paths.
            if (NativeLibrary.TryLoad(platformSpecificName, assembly, searchPath, out var library))
                return library;

            // 3. If that fails, try to load it from the application's 'runtimes' directory for self-contained apps.
            var relativePath = GetLibraryPath(libraryName); // This still gives the full relative path
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);

            if (File.Exists(fullPath) && NativeLibrary.TryLoad(fullPath, out library))
                return library;
            
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
            else
            {
                throw new PlatformNotSupportedException(
                    $"Unsupported operating system: {RuntimeInformation.OSDescription}");
            }

            return Path.Combine(relativeBase, rid, "native", platformSpecificName);
        }
    }
    
    #endregion

    #region Decoder Functions

    [LibraryImport(LibraryName, EntryPoint = "sf_decoder_create")]
    public static partial SafeDecoderHandle CreateDecoder();

    [LibraryImport(LibraryName, EntryPoint = "sf_decoder_init")]
    public static partial FFmpegResult InitializeDecoder(SafeDecoderHandle decoder, ReadCallback onRead, SeekCallback onSeek, IntPtr pUserData,
        SampleFormat targetFormat, out SampleFormat outNativeFormat, out uint outChannels, out uint outSamplerate);

    [LibraryImport(LibraryName, EntryPoint = "sf_decoder_get_length_in_pcm_frames")]
    public static partial long GetLengthInPcmFrames(SafeDecoderHandle decoder);

    [LibraryImport(LibraryName, EntryPoint = "sf_decoder_read_pcm_frames")]
    public static partial FFmpegResult ReadPcmFrames(SafeDecoderHandle decoder, IntPtr pFramesOut, long frameCount, out long outFramesRead);

    [LibraryImport(LibraryName, EntryPoint = "sf_decoder_seek_to_pcm_frame")]
    public static partial FFmpegResult SeekToPcmFrame(SafeDecoderHandle decoder, long frameIndex);

    [LibraryImport(LibraryName, EntryPoint = "sf_decoder_free")]
    public static partial void FreeDecoder(IntPtr decoder);

    #endregion

    #region Encoder Functions

    [LibraryImport(LibraryName, EntryPoint = "sf_encoder_create")]
    public static partial SafeEncoderHandle CreateEncoder();

    [LibraryImport(LibraryName, EntryPoint = "sf_encoder_init", StringMarshalling = StringMarshalling.Utf8)]
    public static partial FFmpegResult InitializeEncoder(SafeEncoderHandle encoder, string formatName, WriteCallback onWrite, IntPtr pUserData,
        SampleFormat sampleFormat, uint channels, uint sampleRate);

    [LibraryImport(LibraryName, EntryPoint = "sf_encoder_write_pcm_frames")]
    public static partial FFmpegResult WritePcmFrames(SafeEncoderHandle encoder, IntPtr pFramesIn, long frameCount, out long outFramesWritten);

    [LibraryImport(LibraryName, EntryPoint = "sf_encoder_free")]
    public static partial void FreeEncoder(IntPtr encoder);

    #endregion

    #region Helper Functions
    
    [LibraryImport(LibraryName, EntryPoint = "sf_result_to_string")]
    [return: MarshalAs(UnmanagedType.LPStr)]
    public static partial string ResultToString(FFmpegResult result);

    #endregion
}