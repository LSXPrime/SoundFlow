using System.Reflection;
using System.Runtime.InteropServices;

namespace SoundFlow.Midi.PortMidi;

/// <summary>
/// Provides P/Invoke declarations for the native PortMidi library.
/// </summary>
internal static unsafe partial class Native
{
    private const string LibraryName = "portmidi";

    [StructLayout(LayoutKind.Sequential)]
    internal struct PmDeviceInfo
    {
        public int StructVersion;
        public nint Interface;
        public nint Name;
        public int Input;
        public int Output;
        public int IsOpened;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PmEvent
    {
        public int Message;
        public int Timestamp;
    }

    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, NativeLibraryResolver.Resolve);
    }
    
    [LibraryImport(LibraryName)]
    public static partial int Pm_Initialize();
    
    [LibraryImport(LibraryName)]
    public static partial int Pm_Terminate();
    
    [LibraryImport(LibraryName)]
    public static partial int Pm_CountDevices();
    
    [LibraryImport(LibraryName)]
    public static partial nint Pm_GetDeviceInfo(int deviceId);
    
    [LibraryImport(LibraryName)]
    public static partial int Pm_OpenInput(out nint stream, int inputDeviceId, nint inputDriverInfo, int bufferSize, nint timeProc, nint timeInfo);
    
    [LibraryImport(LibraryName)]
    public static partial int Pm_OpenOutput(out nint stream, int outputDeviceId, nint outputDriverInfo, int bufferSize, nint timeProc, nint timeInfo, int latency);
    
    [LibraryImport(LibraryName)]
    public static partial int Pm_Close(nint stream);
    
    [LibraryImport(LibraryName)]
    public static partial int Pm_SetFilter(nint stream, int filters);
    
    [LibraryImport(LibraryName)]
    public static partial int Pm_Poll(nint stream);
    
    [LibraryImport(LibraryName)]
    public static partial int Pm_Read(nint stream, PmEvent* buffer, int length);

    [LibraryImport(LibraryName)]
    public static partial int Pm_WriteShort(nint stream, int when, int msg);

    [LibraryImport(LibraryName)]
    public static partial int Pm_WriteSysEx(nint stream, int when, nint msg);
    
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial string Pm_GetErrorText(int errorCode);

    private static class NativeLibraryResolver
    {
        public static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            var platformSpecificName = GetPlatformSpecificLibraryName(libraryName);
            if (NativeLibrary.TryLoad(platformSpecificName, assembly, searchPath, out var library))
                return library;
            
            var relativePath = GetLibraryPath(libraryName);
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
            return File.Exists(fullPath) && NativeLibrary.TryLoad(fullPath, out library) ? library : NativeLibrary.Load(fullPath);
        }

        private static string GetPlatformSpecificLibraryName(string libraryName) =>
            OperatingSystem.IsWindows() ? $"{libraryName}.dll" :
            OperatingSystem.IsMacOS() ? $"lib{libraryName}.dylib" : $"lib{libraryName}.so";

        private static string GetLibraryPath(string libraryName)
        {
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
            
            return Path.Combine("runtimes", rid, "native", platformSpecificName);
        }
    }
}