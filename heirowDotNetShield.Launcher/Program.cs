using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace HeirowDotNetShield.Launcher;

internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HostfxrInitializeForCommandLine(
        int argumentCount,
        nint arguments,
        nint parameters,
        out nint hostContext);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HostfxrRunApp(nint hostContext);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HostfxrClose(nint hostContext);

    [StructLayout(LayoutKind.Sequential)]
    private struct HostfxrInitializeParameters
    {
        internal nuint Size;
        internal nint HostPath;
        internal nint DotnetRoot;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ApplyPreRuntimePolicy();
            var launcherPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("The native launcher path is unavailable.");
            var applicationDll = Path.ChangeExtension(launcherPath, ".dll");
            if (!File.Exists(applicationDll))
            {
                throw new FileNotFoundException("The protected application DLL was not found beside its launcher.", applicationDll);
            }

            var dotnetRoot = FindDotnetRoot();
            var hostfxrPath = FindHostfxr(dotnetRoot);
            return RunManagedApplication(hostfxrPath, dotnetRoot, launcherPath, applicationDll, args);
        }
        catch (Exception ex)
        {
            ShowFatalError(ex.Message);
            return -1;
        }
    }

    private static void ApplyPreRuntimePolicy()
    {
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", null);
        Environment.SetEnvironmentVariable("DOTNET_EnableDiagnostics", "0");
        Environment.SetEnvironmentVariable("DOTNET_EnableDiagnostics_IPC", "0");
        Environment.SetEnvironmentVariable("DOTNET_EnableDiagnostics_Debugger", "0");
        Environment.SetEnvironmentVariable("DOTNET_EnableDiagnostics_Profiler", "0");
        Environment.SetEnvironmentVariable("DOTNET_ENABLE_PROFILING", "0");
        Environment.SetEnvironmentVariable("CORECLR_ENABLE_PROFILING", "0");
    }

    private static string FindDotnetRoot()
    {
        var configured = Environment.GetEnvironmentVariable("DOTNET_ROOT_X64")
            ?? Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
        var registered = key?.GetValue("InstallLocation") as string;
        if (!string.IsNullOrWhiteSpace(registered) && Directory.Exists(registered))
        {
            return registered;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var fallback = Path.Combine(programFiles, "dotnet");
        return Directory.Exists(fallback)
            ? fallback
            : throw new DirectoryNotFoundException("A 64-bit .NET installation was not found.");
    }

    private static string FindHostfxr(string dotnetRoot)
    {
        var fxrRoot = Path.Combine(dotnetRoot, "host", "fxr");
        var candidate = Directory.EnumerateDirectories(fxrRoot)
            .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
            .Where(item => item.Version is not null)
            .OrderByDescending(item => item.Version)
            .Select(item => Path.Combine(item.Path, "hostfxr.dll"))
            .FirstOrDefault(File.Exists);
        return candidate ?? throw new FileNotFoundException("hostfxr.dll was not found in the registered .NET installation.");
    }

    private static Version? ParseVersion(string value)
    {
        var stable = value.Split('-', 2)[0];
        return Version.TryParse(stable, out var version) ? version : null;
    }

    private static int RunManagedApplication(
        string hostfxrPath,
        string dotnetRoot,
        string launcherPath,
        string applicationDll,
        string[] applicationArguments)
    {
        var library = NativeLibrary.Load(hostfxrPath);
        var initialize = Marshal.GetDelegateForFunctionPointer<HostfxrInitializeForCommandLine>(
            NativeLibrary.GetExport(library, "hostfxr_initialize_for_dotnet_command_line"));
        var run = Marshal.GetDelegateForFunctionPointer<HostfxrRunApp>(
            NativeLibrary.GetExport(library, "hostfxr_run_app"));
        var close = Marshal.GetDelegateForFunctionPointer<HostfxrClose>(
            NativeLibrary.GetExport(library, "hostfxr_close"));

        var commandLine = new[] { applicationDll }.Concat(applicationArguments).ToArray();
        var argumentStrings = new nint[commandLine.Length];
        var argumentArray = nint.Zero;
        var hostPathPointer = nint.Zero;
        var dotnetRootPointer = nint.Zero;
        var parametersPointer = nint.Zero;
        var hostContext = nint.Zero;

        try
        {
            for (var index = 0; index < commandLine.Length; index++)
            {
                argumentStrings[index] = Marshal.StringToCoTaskMemUni(commandLine[index]);
            }

            argumentArray = Marshal.AllocCoTaskMem(IntPtr.Size * argumentStrings.Length);
            for (var index = 0; index < argumentStrings.Length; index++)
            {
                Marshal.WriteIntPtr(argumentArray, index * IntPtr.Size, argumentStrings[index]);
            }

            hostPathPointer = Marshal.StringToCoTaskMemUni(launcherPath);
            dotnetRootPointer = Marshal.StringToCoTaskMemUni(dotnetRoot);
            var parameters = new HostfxrInitializeParameters
            {
                Size = (nuint)Marshal.SizeOf<HostfxrInitializeParameters>(),
                HostPath = hostPathPointer,
                DotnetRoot = dotnetRootPointer
            };
            parametersPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<HostfxrInitializeParameters>());
            Marshal.StructureToPtr(parameters, parametersPointer, fDeleteOld: false);

            var initializeResult = initialize(commandLine.Length, argumentArray, parametersPointer, out hostContext);
            if (initializeResult != 0 || hostContext == nint.Zero)
            {
                throw new InvalidOperationException($"The .NET host rejected the protected application (0x{initializeResult:X8}).");
            }

            return run(hostContext);
        }
        finally
        {
            if (hostContext != nint.Zero)
            {
                close(hostContext);
            }

            foreach (var pointer in argumentStrings)
            {
                if (pointer != nint.Zero)
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }

            if (argumentArray != nint.Zero) Marshal.FreeCoTaskMem(argumentArray);
            if (hostPathPointer != nint.Zero) Marshal.FreeCoTaskMem(hostPathPointer);
            if (dotnetRootPointer != nint.Zero) Marshal.FreeCoTaskMem(dotnetRootPointer);
            if (parametersPointer != nint.Zero) Marshal.FreeCoTaskMem(parametersPointer);
            NativeLibrary.Free(library);
        }
    }

    private static void ShowFatalError(string message)
    {
        try
        {
            MessageBox(nint.Zero, message, "heirowDotNetShield", 0x10);
        }
        catch
        {
            // There is no console in the protected GUI launcher.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint window, string text, string caption, uint type);
}
