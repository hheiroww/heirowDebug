using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace heirowDebug.ProjectZ.Tools;

public sealed record ProjectZProcessInfo(int Id, string Name, string WindowTitle, string ExecutablePath)
{
    public override string ToString() => $"{Name} (PID {Id})  {WindowTitle}";
}

public static class InjectorService
{
    public static IReadOnlyList<ProjectZProcessInfo> GetProjectZProcesses()
    {
        var result = new List<ProjectZProcessInfo>();
        foreach (var process in Process.GetProcesses().OrderBy(p => p.ProcessName))
        {
            try
            {
                if (process.Id == Environment.ProcessId) continue;
                var modules = process.Modules.Cast<ProcessModule>().ToArray();
                if (!modules.Any(m => string.Equals(m.ModuleName, "ProjectZ.dll", StringComparison.OrdinalIgnoreCase))) continue;
                var path = process.MainModule?.FileName;
                if (path is not null) result.Add(new(process.Id, process.ProcessName, process.MainWindowTitle, path));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        return result;
    }
    public static async Task<string> RestartAndInjectAsync(ProjectZProcessInfo target, bool recursive = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = AppContext.BaseDirectory;
        var hook = Path.Combine(directory, "heirowDebug.ProjectZ.Hook.dll");
        if (!File.Exists(hook) || !File.Exists(Path.Combine(directory, "heirowDebug.ProjectZ.dll"))) throw new FileNotFoundException("The Project-Z injector payload is incomplete.");
        // Validate all payload and target identity before asking the process to close.
        using var process = Process.GetProcessById(target.Id);
        if (!string.Equals(process.MainModule?.FileName, target.ExecutablePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The selected process identity changed. Refresh the process list.");
        if (!process.CloseMainWindow() && !CloseNativeWindow(process.Id)) throw new InvalidOperationException("The target could not close normally. Close it yourself after saving your work.");
        await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        var statusFolder = Path.Combine(Path.GetTempPath(), "heirowDebug", "projectz-injections"); Directory.CreateDirectory(statusFolder);
        var statusFile = Path.Combine(statusFolder, System.Guid.NewGuid().ToString("N") + ".status");
        var start = new ProcessStartInfo(target.ExecutablePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target.ExecutablePath)! };
        var existing = start.Environment.TryGetValue("DOTNET_STARTUP_HOOKS", out var hooks) ? hooks : null;
        start.Environment["DOTNET_STARTUP_HOOKS"] = string.IsNullOrEmpty(existing) ? hook : existing + Path.PathSeparator + hook;
        start.Environment["heirowDebug_PROJECTZ_PAYLOAD"] = directory;
        start.Environment["heirowDebug_STATUS_FILE"] = statusFile;
        start.Environment["heirowDebug_RECURSIVE"] = recursive ? "1" : "0";
        using var launched = Process.Start(start) ?? throw new InvalidOperationException("Windows did not launch the target.");
        progress?.Report($"Started PID {launched.Id}. Waiting for its Project-Z scene…");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(statusFile))
                {
                    string status; try { status = await File.ReadAllTextAsync(statusFile, cancellationToken); } catch (IOException) { await Task.Delay(100, cancellationToken); continue; }
                    if (status.StartsWith("READY|", StringComparison.Ordinal)) return $"Injected into {target.Name} (PID {launched.Id}). " + status.Split('|', 4)[3];
                    if (status.StartsWith("ERROR|", StringComparison.Ordinal)) throw new InvalidOperationException(status.Split('|', 4)[3]);
                }
                if (launched.HasExited) throw new InvalidOperationException($"The target exited with code {launched.ExitCode} before the debugger was ready.");
                await Task.Delay(100, cancellationToken);
            }
            throw new TimeoutException($"PID {launched.Id} is still running but did not report a ready Project-Z debugger.");
        }
        finally { try { File.Delete(statusFile); } catch (IOException) { } }
    }
    static bool CloseNativeWindow(int processId)
    {
        bool posted = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == processId) posted |= PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return posted;
    }
    delegate bool EnumWindowProc(IntPtr hwnd, IntPtr state);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback, IntPtr state);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
}
