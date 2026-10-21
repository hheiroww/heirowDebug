using System.Reflection;
using System.Runtime.Loader;

// The runtime requires this exact global entry point. Keep framework references
// out of Initialize so dependencies resolve before Project-Z code is JITted.
internal static class StartupHook
{
    public static void Initialize()
    {
        var directory = Environment.GetEnvironmentVariable("heirowDebug_PROJECTZ_PAYLOAD") ?? Path.GetDirectoryName(typeof(StartupHook).Assembly.Location)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name);
            var candidate = Path.Combine(directory, name.Name + ".dll");
            return loaded ?? (File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null);
        };
        var path = typeof(StartupHook).Assembly.Location;
        var hooks = Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS");
        if (!string.IsNullOrEmpty(hooks)) Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", string.Join(Path.PathSeparator, hooks.Split(Path.PathSeparator).Where(h => !string.Equals(h, path, StringComparison.OrdinalIgnoreCase))));
        var thread = new Thread(() =>
        {
            try { Activate(); } catch (Exception ex) { Report("ERROR", (ex.InnerException ?? ex).Message); }
        }) { IsBackground = true, Name = "heirowDebug Project-Z discovery" };
        thread.Start();
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Activate()
    {
        // Delay loading the native framework until the application has loaded its
        // own copy. The hook must not replace the target's renderer/dependencies.
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "ProjectZ")) { Attach(); return; }
            Thread.Sleep(100);
        }
        Report("ERROR", "Timed out waiting for Project-Z to load.");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Attach()
    {
        heirowDebug.ProjectZ.Utils.FramesPerSecond = 15;
        var deadline = DateTime.UtcNow.AddMinutes(2);
        Report("WAITING", "Discovering live Project-Z games, scenes and static application roots.");
        while (DateTime.UtcNow < deadline)
        {
            foreach (var root in heirowDebug.ProjectZ.RootDiscovery.FindRoots())
            {
                // Scene frame callbacks are the only place that native controls
                // and graphics may be constructed inside an existing application.
                var scene = FindScene(root);
                if (scene is null || !scene.isInitialized) continue;
                int activated = 0;
                global::ProjectZ.Shared.Drawing.Scene.FrameUpdatedEventHandler? onFrame = null;
                onFrame = _ =>
                {
                    if (Interlocked.Exchange(ref activated, 1) != 0) return;
                    scene.FrameUpdated -= onFrame;
                    try
                    {
                        var roots = heirowDebug.ProjectZ.RootDiscovery.FindRoots();
                        foreach (var live in roots)
                        {
                            var watcher = heirowDebug.ProjectZ.DebugWatcher.WatchWindow(live, Environment.GetEnvironmentVariable("heirowDebug_RECURSIVE") == "1");
                            watcher.AttachScene(scene); watcher.StartSession();
                        }
                        heirowDebug.ProjectZ.DebugWatcher.CreateDebugWindow();
                        Report("READY", $"Watching {roots.Length} Project-Z roots on their scene thread.");
                    }
                    catch (Exception ex) { Report("ERROR", (ex.InnerException ?? ex).Message); }
                };
                scene.FrameUpdated += onFrame;
                return;
            }
            Thread.Sleep(100);
        }
        Report("ERROR", "No live scene could be discovered. Register a Game/Scene root with RootDiscovery.Register, or expose it in an application static field.");
    }
    static global::ProjectZ.Shared.Drawing.Scene? FindScene(object root)
    {
        if (root is global::ProjectZ.Shared.Drawing.Scene scene) return scene;
        if (root is global::ProjectZ.Shared.Drawing.SceneManager manager) return manager.ActiveScene;
        foreach (var field in root.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            try { if (field.GetValue(root) is global::ProjectZ.Shared.Drawing.Scene s) return s; if (field.GetValue(root) is global::ProjectZ.Shared.Drawing.SceneManager m) return m.ActiveScene; } catch { }
        return null;
    }
    static void Report(string state, string detail)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("heirowDebug_STATUS_FILE") is { Length: > 0 } path)
                File.WriteAllText(path, $"{state}|{Environment.ProcessId}|{DateTimeOffset.UtcNow:O}|{detail}");
        }
        catch { /* Diagnostics must not terminate the target. */ }
    }
}
