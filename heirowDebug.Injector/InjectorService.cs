using System.Diagnostics;
using System.IO;

namespace heirowDebugInjector;

public sealed record WpfProcessInfo(int Id, string Name, string WindowTitle, string ExecutablePath);

internal static class InjectorService
{
    private const string HookFileName = "heirowDebug.Injector.Hook.dll";

    internal static IReadOnlyList<WpfProcessInfo> GetWpfProcesses()
    {
        var found = new List<WpfProcessInfo>();
        foreach (var process in Process.GetProcesses().OrderBy(process => process.ProcessName))
        {
            try
            {
                if (process.Id == Environment.ProcessId ||
                    !process.Modules.Cast<ProcessModule>().Any(module =>
                        string.Equals(module.ModuleName, "PresentationFramework.dll", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var executablePath = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executablePath))
                {
                    found.Add(new WpfProcessInfo(process.Id, process.ProcessName, process.MainWindowTitle, executablePath));
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Protected and exiting processes are not candidates for this user-session tool.
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }

    internal static async Task<string> RestartAndInjectAsync(
        WpfProcessInfo target,
        string? rootType,
        bool recursive,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await StopTargetAsync(target, progress, cancellationToken);

        var payloadDirectory = AppContext.BaseDirectory;
        var hookPath = Path.Combine(payloadDirectory, HookFileName);
        var heirowDebugPath = Path.Combine(payloadDirectory, "heirowDebug.WPF.dll");
        if (!File.Exists(hookPath) || !File.Exists(heirowDebugPath))
        {
            throw new InvalidOperationException("The injector payload is incomplete. Rebuild heirowDebug.Injector before running heirowDebug.exe.");
        }

        var statusDirectory = Path.Combine(Path.GetTempPath(), "heirowDebug", "injections");
        Directory.CreateDirectory(statusDirectory);
        var statusPath = Path.Combine(statusDirectory, $"{Guid.NewGuid():N}.status");

        var startInfo = new ProcessStartInfo(target.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(target.ExecutablePath)!
        };
        var existingHooks = startInfo.Environment.TryGetValue("DOTNET_STARTUP_HOOKS", out var hooks) ? hooks : null;
        startInfo.Environment["DOTNET_STARTUP_HOOKS"] = string.IsNullOrWhiteSpace(existingHooks)
            ? hookPath
            : existingHooks + Path.PathSeparator + hookPath;
        startInfo.Environment["heirowDebug_PAYLOAD_DIRECTORY"] = payloadDirectory;
        startInfo.Environment["heirowDebug_STATUS_FILE"] = statusPath;
        startInfo.Environment["heirowDebug_RECURSIVE"] = recursive ? "1" : "0";
        if (!string.IsNullOrWhiteSpace(rootType))
        {
            startInfo.Environment["heirowDebug_ROOT_TYPE"] = rootType;
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows did not restart the target process.");
        progress?.Report($"Started {target.Name} as PID {process.Id}; waiting for the injected watcher...");

        var deadline = DateTime.UtcNow.AddSeconds(60);
        string? lastStatus = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(statusPath))
            {
                lastStatus = TryReadStatus(statusPath);
                if (lastStatus.StartsWith("READY|", StringComparison.Ordinal))
                {
                    return $"Injected successfully into {target.Name} (PID {process.Id}). {StatusDetail(lastStatus)}";
                }

                if (lastStatus.StartsWith("ERROR|", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(StatusDetail(lastStatus));
                }
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException($"The restarted target exited with code {process.ExitCode}. Last status: {lastStatus ?? "none"}");
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException($"The target is still running as PID {process.Id}, but heirowDebug did not report ready. Last status: {lastStatus ?? "none"}");
    }

    private static async Task StopTargetAsync(WpfProcessInfo target, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var process = Process.GetProcessById(target.Id);
        if (process.HasExited)
        {
            return;
        }

        if (process.CloseMainWindow())
        {
            var gracefulDeadline = DateTime.UtcNow.AddSeconds(3);
            while (!process.HasExited && DateTime.UtcNow < gracefulDeadline)
            {
                await Task.Delay(100, cancellationToken);
                process.Refresh();
            }
        }

        if (!process.HasExited)
        {
            progress?.Report($"{target.Name} did not close through its main window; ending PID {target.Id} before restart...");
            process.Kill(entireProcessTree: false);
            await process.WaitForExitAsync(cancellationToken);
        }
    }

    private static string TryReadStatus(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static string StatusDetail(string status)
    {
        var fields = status.Split('|', 4);
        return fields.Length == 4 ? fields[3] : status;
    }
}
