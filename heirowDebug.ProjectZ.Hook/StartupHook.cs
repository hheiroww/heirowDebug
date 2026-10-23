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
            // Only supply debugger code. The target owns its bundled/Fody-loaded
            // framework, renderer and all other dependencies.
            if (name.Name != "heirowDebug.ProjectZ") return null;
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
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            if (loaded.Any(a => a.GetName().Name == "ProjectZ") && loaded.Any(a => a.GetName().Name == "MonoGame.Framework")) { Attach(deadline); return; }
            Thread.Sleep(100);
        }
        Report("ERROR", "Timed out waiting for Project-Z to load.");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Attach(DateTime deadline)
    {
        heirowDebug.ProjectZ.Utils.FramesPerSecond = 15;
        Report("WAITING", "Discovering live Project-Z games, scenes and static application roots.");
        int activated = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriptions = new List<(global::ProjectZ.Shared.Drawing.Scene Scene, global::ProjectZ.Shared.Drawing.Scene.FrameUpdatedEventHandler Handler)>();
        try
        {
            while (DateTime.UtcNow < deadline && !completed.Task.IsCompleted)
            {
                foreach (var root in OrderedRoots())
                {
                    // Subscribe to each candidate: an initialized but inactive
                    // scene must not prevent a running scene from accepting us.
                    var scene = heirowDebug.ProjectZ.RootDiscovery.FindScene(root);
                    if (scene is null || !scene.isInitialized || subscriptions.Any(s => ReferenceEquals(s.Scene, scene))) continue;
                    global::ProjectZ.Shared.Drawing.Scene.FrameUpdatedEventHandler onFrame = _ =>
                    {
                        if (Interlocked.CompareExchange(ref activated, 1, 0) != 0) return;
                        try
                        {
                            var roots = OrderedRoots().Where(r => ReferenceEquals(heirowDebug.ProjectZ.RootDiscovery.FindScene(r), scene)).ToArray();
                            foreach (var live in roots)
                            {
                                var watcher = heirowDebug.ProjectZ.DebugWatcher.WatchWindow(live, Environment.GetEnvironmentVariable("heirowDebug_RECURSIVE") == "1");
                                watcher.AttachScene(scene); watcher.StartSession();
                            }
                            heirowDebug.ProjectZ.DebugWatcher.CreateDebugWindow();
                            if (heirowDebug.ProjectZ.DebugWatcher.DebugWindow?.IsVisible != true) throw new InvalidOperationException("The debugger did not create a visible native window.");
                            Report("READY", $"Watching {roots.Length} Project-Z roots on their scene thread.");
                        }
                        catch (Exception ex) { Report("ERROR", (ex.InnerException ?? ex).Message); }
                        finally { completed.TrySetResult(); }
                    };
                    subscriptions.Add((scene, onFrame));
                    scene.FrameUpdated += onFrame;
                }
                completed.Task.Wait(100);
            }
            if (!completed.Task.IsCompleted && Interlocked.CompareExchange(ref activated, -1, 0) == 0) Report("ERROR", "No updating Project-Z scene could be discovered. Keep the application open and restore its main window before retrying.");
        }
        finally { foreach (var subscription in subscriptions) subscription.Scene.FrameUpdated -= subscription.Handler; }
    }
    static object[] OrderedRoots()
    {
        var preferred = Environment.GetEnvironmentVariable("heirowDebug_ROOT_TYPE");
        return heirowDebug.ProjectZ.RootDiscovery.FindRoots().OrderBy(root => !string.IsNullOrWhiteSpace(preferred) && (root.GetType().Name == preferred || root.GetType().FullName == preferred) ? 0 : 1).ToArray();
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
