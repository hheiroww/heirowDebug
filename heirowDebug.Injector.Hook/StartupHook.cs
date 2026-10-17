using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Windows;

// The .NET host looks for this exact global type and method.
internal static class StartupHook
{
    public static void Initialize()
    {
        try
        {
            heirowDebugInjector.InjectedAgent.Initialize();
        }
        catch (Exception ex)
        {
            heirowDebugInjector.InjectedAgent.Report("ERROR", ex);
        }
    }
}

namespace heirowDebugInjector
{
internal static class InjectedAgent
{
    private const string PayloadDirectoryVariable = "heirowDebug_PAYLOAD_DIRECTORY";
    private const string RootTypeVariable = "heirowDebug_ROOT_TYPE";
    private const string RecursiveVariable = "heirowDebug_RECURSIVE";
    private const string StatusFileVariable = "heirowDebug_STATUS_FILE";
    private static int _started;
    private static string? _payloadDirectory;

    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _payloadDirectory = Environment.GetEnvironmentVariable(PayloadDirectoryVariable);
        if (string.IsNullOrWhiteSpace(_payloadDirectory))
        {
            _payloadDirectory = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location);
        }

        AssemblyLoadContext.Default.Resolving += ResolvePayloadAssembly;
        PreventChildProcessInjection();

        var worker = new Thread(WaitForWpfAndInject)
        {
            IsBackground = true,
            Name = "heirowDebug injector"
        };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
    }

    private static Assembly? ResolvePayloadAssembly(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrWhiteSpace(_payloadDirectory) || string.IsNullOrWhiteSpace(name.Name))
        {
            return null;
        }

        var existing = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var candidate = Path.Combine(_payloadDirectory, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    }

    private static void WaitForWpfAndInject()
    {
        try
        {
            var requestedType = Environment.GetEnvironmentVariable(RootTypeVariable);
            var overrideDeadline = DateTime.UtcNow.AddSeconds(10);
            Report("WAITING", string.IsNullOrWhiteSpace(requestedType)
                ? "Waiting for WPF and discovering every Application.Current window."
                : $"Waiting for preferred WPF window type '{requestedType}' (all windows remain available)." );
            var deadline = DateTime.UtcNow.AddMinutes(2);
            while (DateTime.UtcNow < deadline)
            {
                var application = Application.Current;
                if (application?.Dispatcher is not null && !application.Dispatcher.HasShutdownStarted)
                {
                    Window[] roots = [];
                    var allowOverrideFallback = string.IsNullOrWhiteSpace(requestedType) || DateTime.UtcNow >= overrideDeadline;
                    application.Dispatcher.Invoke(() => roots = SelectRoots(application, requestedType, allowOverrideFallback));
                    if (roots.Length > 0)
                    {
                        application.Dispatcher.Invoke(() => ActivateheirowDebug(roots));
                        return;
                    }
                }

                Thread.Sleep(100);
            }

            Report("ERROR", "Timed out waiting for a matching WPF window.");
        }
        catch (Exception ex)
        {
            Report("ERROR", Unwrap(ex));
        }
    }

    private static Window[] SelectRoots(Application application, string? requestedType, bool allowOverrideFallback)
    {
        var windows = application.Windows.Cast<Window>()
            .Where(window => window is not null && window.GetType().Assembly != typeof(InjectedAgent).Assembly)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(requestedType))
        {
            var requested = windows.FirstOrDefault(window =>
                string.Equals(window.GetType().FullName, requestedType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(window.GetType().Name, requestedType, StringComparison.OrdinalIgnoreCase));
            if (requested is null && !allowOverrideFallback)
            {
                return [];
            }

            if (requested is not null)
            {
                return windows
                    .OrderByDescending(window => ReferenceEquals(window, requested))
                    .ThenByDescending(window => ReferenceEquals(window, application.MainWindow))
                    .ThenByDescending(window => window.IsLoaded && window.IsVisible)
                    .ThenByDescending(window => Math.Max(0, window.ActualWidth) * Math.Max(0, window.ActualHeight))
                    .ToArray();
            }
        }

        return windows
            .OrderByDescending(window => ReferenceEquals(window, application.MainWindow))
            .ThenByDescending(window => window.IsLoaded && window.IsVisible)
            .ThenByDescending(window => Math.Max(0, window.ActualWidth) * Math.Max(0, window.ActualHeight))
            .ToArray();
    }

    private static void ActivateheirowDebug(IReadOnlyList<Window> roots)
    {
        if (string.IsNullOrWhiteSpace(_payloadDirectory))
        {
            throw new InvalidOperationException("The heirowDebug payload directory was not configured.");
        }

        var assemblyPath = Path.Combine(_payloadDirectory, "heirowDebug.WPF.dll");
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("heirowDebug.WPF.dll was not found in the injector payload.", assemblyPath);
        }

        var heirowDebugAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "heirowDebug.WPF", StringComparison.OrdinalIgnoreCase))
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

        var watcherType = heirowDebugAssembly.GetType("heirowDebug.WPF.DebugWatcher", throwOnError: true)!;
        var recursive = !string.Equals(Environment.GetEnvironmentVariable(RecursiveVariable), "0", StringComparison.Ordinal);
        ConfigureSafeInjectedSampling(heirowDebugAssembly);
        var watchWindow = watcherType.GetMethod("WatchWindow", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(watcherType.FullName, "WatchWindow");
        var startSession = watcherType.GetMethod("StartSession", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(watcherType.FullName, "StartSession");
        var watchedRoots = new List<Window>();
        foreach (var root in roots)
        {
            var watcher = watchWindow.Invoke(null, [root, recursive]);
            if (watcher is null)
            {
                continue;
            }
            startSession.Invoke(watcher, null);
            watchedRoots.Add(root);
        }
        if (watchedRoots.Count == 0)
        {
            throw new InvalidOperationException("heirowDebug could not create a watcher within its configured safety limits.");
        }

        watcherType.GetMethod("CreateDebugWindow", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null);

        var debugWindow = watcherType.GetProperty("DebugWindow", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null) as Window;
        if (debugWindow is not null)
        {
            var process = Process.GetCurrentProcess();
            debugWindow.Title = $"heirowDebug - {process.ProcessName} (PID {process.Id})";
            debugWindow.Activate();
        }

        var rootNames = string.Join(", ", watchedRoots.Select(root => root.GetType().FullName));
        Report("READY", $"root={watchedRoots[0].GetType().FullName}; roots={watchedRoots.Count}; skipped={roots.Count - watchedRoots.Count}; windows={rootNames}; recursive={recursive}; assembly={heirowDebugAssembly.Location}");
    }

    private static void ConfigureSafeInjectedSampling(Assembly heirowDebugAssembly)
    {
        // An injected debugger shares the application's UI and thread pool. Keep
        // its default polling budget deliberately below an in-app debug build;
        // callers can still change Utils.FramesPerSecond after startup.
        var utilsType = heirowDebugAssembly.GetType("heirowDebug.WPF.Utils", throwOnError: false);
        utilsType?.GetProperty("FramesPerSecond", BindingFlags.Public | BindingFlags.Static)
            ?.SetValue(null, 15);
    }

    private static void PreventChildProcessInjection()
    {
        var hookPath = typeof(StartupHook).Assembly.Location;
        var hooks = Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS");
        if (string.IsNullOrWhiteSpace(hooks))
        {
            return;
        }

        var remaining = hooks.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(value => !string.Equals(Path.GetFullPath(value), hookPath, StringComparison.OrdinalIgnoreCase));
        Environment.SetEnvironmentVariable("DOTNET_STARTUP_HOOKS", string.Join(Path.PathSeparator, remaining));
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null })
        {
            exception = exception.InnerException;
        }

        return exception;
    }

    internal static void Report(string state, Exception exception) =>
        Report(state, $"{exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");

    internal static void Report(string state, string detail)
    {
        try
        {
            var statusPath = Environment.GetEnvironmentVariable(StatusFileVariable);
            if (string.IsNullOrWhiteSpace(statusPath))
            {
                return;
            }

            var line = new StringBuilder()
                .Append(state).Append('|')
                .Append(Environment.ProcessId).Append('|')
                .Append(DateTimeOffset.UtcNow.ToString("O")).Append('|')
                .Append(detail)
                .ToString();
            File.WriteAllText(statusPath, line);
        }
        catch
        {
            // Diagnostics must never terminate the host application.
        }
    }
}
}
