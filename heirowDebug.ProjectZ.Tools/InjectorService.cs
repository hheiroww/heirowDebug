using System.Diagnostics;

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
    public static async Task<string> RestartAndInjectAsync(ProjectZProcessInfo target, bool recursive = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default, string? preferredRootType = null)
    {
        var directory = AppContext.BaseDirectory;
        var hook = Path.Combine(directory, "heirowDebug.ProjectZ.Hook.dll");
        if (!File.Exists(hook) || !File.Exists(Path.Combine(directory, "heirowDebug.ProjectZ.dll"))) throw new FileNotFoundException("The Project-Z injector payload is incomplete.");
        // Validate all payload and target identity before asking the process to close.
        using var process = Process.GetProcessById(target.Id);
        if (!string.Equals(process.MainModule?.FileName, target.ExecutablePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The selected process identity changed. Refresh the process list.");
        bool originalRunning = false;
        progress?.Report($"Requesting a normal close of {target.Name} (PID {target.Id})…");
        if (process.CloseMainWindow() || CloseNativeWindow(process.Id))
        {
            try { await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); }
            catch (TimeoutException) { originalRunning = !process.HasExited; }
        }
        else originalRunning = !process.HasExited;
        if (originalRunning) progress?.Report($"PID {target.Id} kept running after the close request. Opening a separate debug instance and preserving the original process.");
        var statusFolder = Path.Combine(Path.GetTempPath(), "heirowDebug", "projectz-injections"); Directory.CreateDirectory(statusFolder);
        var statusFile = Path.Combine(statusFolder, System.Guid.NewGuid().ToString("N") + ".status");
        var start = new ProcessStartInfo(target.ExecutablePath) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target.ExecutablePath)! };
        var existing = start.Environment.TryGetValue("DOTNET_STARTUP_HOOKS", out var hooks) ? hooks : null;
        start.Environment["DOTNET_STARTUP_HOOKS"] = string.IsNullOrEmpty(existing) ? hook : existing + Path.PathSeparator + hook;
        start.Environment["heirowDebug_PROJECTZ_PAYLOAD"] = directory;
        start.Environment["heirowDebug_STATUS_FILE"] = statusFile;
        start.Environment["heirowDebug_RECURSIVE"] = recursive ? "1" : "0";
        start.Environment["heirowDebug_ROOT_TYPE"] = preferredRootType ?? "";
        using var launched = Process.Start(start) ?? throw new InvalidOperationException("Windows did not launch the target.");
        progress?.Report($"Started PID {launched.Id}. Waiting for its Project-Z scene…");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(75);
            string? previousStatus = null;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(statusFile))
                {
                    string status; try { status = await File.ReadAllTextAsync(statusFile, cancellationToken); } catch (IOException) { await Task.Delay(100, cancellationToken); continue; }
                    var parts = status.Split('|', 4);
                    if (parts.Length != 4) { await Task.Delay(50, cancellationToken); continue; }
                    if (parts[0] == "READY") return $"Injected into {target.Name} (PID {launched.Id}). " + parts[3] + (originalRunning ? $" Original PID {target.Id} remains running." : "");
                    if (parts[0] == "ERROR") throw new InvalidOperationException(parts[3]);
                    if (status != previousStatus) { progress?.Report(parts[3]); previousStatus = status; }
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
