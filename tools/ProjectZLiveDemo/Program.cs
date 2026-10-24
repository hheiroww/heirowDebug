using System.Diagnostics;
using System.Text.Json;
using heirowDebug.ProjectZ;
using Microsoft.Xna.Framework;
using NativeWindow = heirowDebug.ProjectZ.NativeWindow;

// Record the production native inspector with a synthetic, changing workload.
// No desktop capture or application/account data enters the public demo asset.
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Specify an empty frame output directory.");
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        if (Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Frame directory must be empty.");
        using var host = new NativeWindow("Project-Z demo telemetry", 980, 590);
        var telemetry = new Telemetry();
        DebugWatcher? watcher = null;
        DebugWindow? inspector = null;
        var clock = Stopwatch.StartNew();
        var loads = new List<double>();
        var selections = new List<string>();
        int hostFrames = 0, captured = 0, phase = -1;
        var nextCapture = TimeSpan.FromSeconds(2);
        host.Ready += () =>
        {
            watcher = new DebugWatcher(telemetry, false, "Live Project-Z telemetry");
            watcher.AttachScene(host.Scene); watcher.StartSession();
            host.Label("Production native inspector · sample telemetry", 25, 25, 900, 18);
        };
        host.Frame += () =>
        {
            double t = clock.Elapsed.TotalSeconds;
            telemetry.LoadPercent = Math.Round(50 + Math.Sin(t * 1.8) * 28 + Math.Sin(t * 4.1) * 8, 1);
            telemetry.RequestsPerSecond = (int)(90 + Math.Cos(t * 1.3) * 35);
            telemetry.IsBusy = (int)(t * 1.5) % 2 == 0;
            telemetry.Position = new((float)(50 + Math.Cos(t * 1.8) * 32), (float)(50 + Math.Sin(t * 2.3) * 25));
            telemetry.Status = "Live native scene · " + (telemetry.IsBusy ? "Processing" : "Ready");
            if (++hostFrames == 10)
            {
                inspector = new DebugWindow { Title = "heirowDebug Project-Z · live inspection" };
                inspector.Show();
            }
            if (inspector?.IsVisible != true || clock.Elapsed < nextCapture) return;
            int nextPhase = captured < 40 ? 0 : captured < 70 ? 1 : 2;
            if (nextPhase != phase)
            {
                var member = nextPhase == 0 ? nameof(Telemetry.LoadPercent) : nextPhase == 1 ? nameof(Telemetry.Position) : nameof(Telemetry.IsBusy);
                inspector.Select(watcher!, watcher!.Values.Single(value => value.Name == member));
                if (!inspector.SelectedValue!.TryEnablePropertyHooks(out var hookError)) throw new InvalidOperationException("Demo property hook failed: " + hookError);
                var mode = nextPhase == 1 ? VisualizationMode.SpatialOverlay : VisualizationMode.Auto;
                for (int attempt = 0; inspector.VisualizationMode != mode && attempt < 4; attempt++) inspector.DispatchClick(380, 75);
                if (inspector.VisualizationMode != mode) throw new InvalidOperationException("The production View control did not change visualization mode.");
                selections.Add(member); phase = nextPhase;
                nextCapture = clock.Elapsed + TimeSpan.FromMilliseconds(200);
                return;
            }
            if (captured == 20)
            {
                inspector.DispatchDrag(470, 738, 620, 738);
                inspector.DispatchDrag(1135, 738, 1070, 738);
            }
            if (captured == 29)
            {
                inspector.DispatchDrag(620, 738, 470, 738);
                inspector.DispatchDrag(1070, 738, 1135, 738);
            }
            inspector.SaveFrame(Path.Combine(output, $"frame-{captured:0000}.png"));
            loads.Add(telemetry.LoadPercent); captured++;
            nextCapture = clock.Elapsed + TimeSpan.FromMilliseconds(100);
            if (captured < 96) return;
            File.WriteAllText(Path.Combine(output, "capture.json"), JsonSerializer.Serialize(new
            {
                renderer = "Production heirowDebug.ProjectZ native inspector",
                frames = captured, intervalMilliseconds = 100, selections,
                antiAliasing = host.Scene.Quality.AntiAliasingSamples,
                technique = host.Scene.Quality.AntiAliasingTechnique,
                anisotropy = host.Scene.Quality.Sampler.MaxAnisotropy,
                loadMinimum = loads.Min(), loadMaximum = loads.Max()
            }, new JsonSerializerOptions { WriteIndented = true }));
            inspector.Dispose(); watcher!.Dispose(); host.Exit();
        };
        host.Run();
        Console.WriteLine($"Recorded {captured} native frames with 16x AA/AF.");
        return captured == 96 ? 0 : 1;
    }
    sealed class Telemetry
    {
        public double LoadPercent
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set;
        }
        public int RequestsPerSecond;
        public bool IsBusy
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set;
        }
        public Vector2 Position
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set;
        }
        public string Status = "Starting native telemetry";
    }
}
