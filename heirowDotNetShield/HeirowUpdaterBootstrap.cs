#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;

namespace SocketJack;

/// <summary>Optional app-root updater integration. Call StartAfterExit once during app startup.</summary>
public static class HeirowUpdaterBootstrap
{
    public static bool StartAfterExit(string? bearerToken = null, string? sessionCookie = null, string? appRoot = null) => Start("after-exit", bearerToken, sessionCookie, appRoot);
    public static bool CheckForUpdates(string? bearerToken = null, string? sessionCookie = null, string? appRoot = null) => Start("check-for-updates", bearerToken, sessionCookie, appRoot);

    private static bool Start(string command, string? bearerToken, string? sessionCookie, string? appRoot)
    {
        try
        {
            var host = Environment.ProcessPath;
            var root = Path.GetFullPath(appRoot ?? (host is not null && !Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(host)! : AppContext.BaseDirectory));
            var updater = Path.Combine(root, "heirowUpdater.exe");
            var configuration = Path.Combine(root, "heirowUpdater.json");
            if (!File.Exists(updater) || !File.Exists(configuration)) return false;
            using var application = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
            start.ArgumentList.Add(command);
            start.ArgumentList.Add("--config"); start.ArgumentList.Add(configuration);
            start.ArgumentList.Add("--application-pid"); start.ArgumentList.Add(application.Id.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--application-start"); start.ArgumentList.Add(application.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
            if (host is not null && !Path.GetFullPath(host).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { start.ArgumentList.Add("--application-host"); start.ArgumentList.Add(Path.GetFullPath(host)); }
            if (bearerToken is not null) start.Environment["HEIROWUPDATER_TOKEN"] = bearerToken;
            if (sessionCookie is not null) start.Environment["HEIROWUPDATER_COOKIE"] = sessionCookie;
            using var companion = Process.Start(start);
            return companion is not null;
        }
        catch { return false; } // Optional updating must never stop the application from opening.
    }
}
