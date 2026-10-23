using heirowDebug.ProjectZ;
using Microsoft.Xna.Framework;
using ProjectZ.Shared.Drawing;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using NativeWindow = heirowDebug.ProjectZ.NativeWindow;

static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args is ["--runtime", var result]) { Runtime(result); return 0; }
            if (args is ["--protected-runtime", var executable, var resultFile]) { ProtectedRuntime(executable, resultFile); return 0; }
            Core(); Console.WriteLine("PASS bounded sampling, snapshots, editing, cycles, static targets, profiles and native visualization types"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    sealed class Sample
    {
        public double Number = 7;
        public bool Boolean = true;
        public List<int> Items = [1, 2, 3];
        public Dictionary<string, int> Dictionary = new() { ["first"] = 1 };
        public Vector2 Point = new(10, 20);
        public Rectangle Rectangle = new(1, 2, 30, 40);
        public Color Tint = Color.Coral;
        public Sample? Cycle;
        public int Reads;
        public int SideEffect { get { Reads++; return Reads; } }
        public int Failure => throw new InvalidOperationException("getter failure");
        public string Text { get; set; } = "before";
        public string LongLabel = "Long watcher text must stay in the tree column: " + new string('W', 180);
    }
    static class Statics { public static int Value = 12; public static int GetterReads; public static object Deferred { get { GetterReads++; return new object(); } } }
    static void Core()
    {
        Utils.Profile = Utils.DebugProfile.PowerSaving;
        DebugWatcher.MaximumMembersPerPass = 192;
        var sample = new Sample(); sample.Cycle = sample;
        using var watcher = new DebugWatcher(sample);
        Check(sample.Reads == 0, "Construction invoked a property getter.");
        watcher.isEnabled = true; watcher.SampleNow();
        var number = watcher.Values.Single(v => v.Name == nameof(Sample.Number));
        Check((double)number.Value! == 7, "Numeric field was not sampled.");
        sample.Number = 9; watcher.SampleNow();
        Check(number.Timeline.GetSnapshots().Length == 2, "Changes were not recorded.");
        number.ApplyAsync("15.5").GetAwaiter().GetResult(); Check(sample.Number == 15.5, "Live field write failed.");
        var text = watcher.Values.Single(v => v.Name == nameof(Sample.Text)); text.ApplyAsync("after").GetAwaiter().GetResult(); Check(sample.Text == "after", "Property write failed.");
        var items = watcher.Values.Single(v => v.Name == nameof(Sample.Items)); sample.Items[0] = 42; watcher.SampleNow();
        Check(((string[])items.Timeline.GetSnapshots()[0].Value!)[0] == "1", "Collection snapshot changed with its source.");
        using var collection = watcher.Expand(items)!;
        Check(collection is not null && collection.Values.Count == 3, "Collection entries did not expand.");
        collection!.isEnabled = true; collection.SampleNow();
        collection.Values[1].ApplyAsync("99").GetAwaiter().GetResult(); Check(sample.Items[1] == 99, "Collection element editing failed.");
        sample.Items.Add(4); collection.SampleNow(); Check(collection.Values.Count == 4, "New collection entries were not discovered.");
        sample.Items.RemoveAt(3); collection.SampleNow(); Check(collection.Values.Count == 3, "Removed collection entries were retained.");
        var dictionary = watcher.Values.Single(v => v.Name == nameof(Sample.Dictionary));
        Check(dictionary.Error is null && dictionary.Timeline.GetSnapshots().Length > 0, "Dictionary snapshot failed.");
        Check((Vector2)DebugValue.ConvertText("3,4", typeof(Vector2))! == new Vector2(3, 4), "Native geometry conversion failed.");
        using var geometry = watcher.Expand(watcher.Values.Single(v => v.Name == nameof(Sample.Point)))!;
        Check(geometry is not null && geometry.Values.Any(v => v.Name == "X"), "Native structs did not expand.");
        geometry!.Values.Single(v => v.Name == "X").ApplyAsync("17").GetAwaiter().GetResult(); Check(sample.Point.X == 17, "Nested struct edit did not propagate to the live parent.");
        Check(watcher.Values.Single(v => v.Name == nameof(Sample.Failure)).Error == "getter failure", "Getter failures were not isolated.");
        Check(watcher.Expand(watcher.Values.Single(v => v.Name == nameof(Sample.Cycle))) is null, "Ancestor cycle was expanded.");
        var reads = Statics.GetterReads;
        using var statics = DebugWatcher.WatchStaticType(typeof(Statics)); Check(Statics.GetterReads == reads, "Static browsing ran getters.");
        statics.isEnabled = true; statics.SampleNow();
        Check((int)statics.Values.Single(v => v.Name == nameof(Statics.Value)).Value! == 12, "Static value was not sampled.");
        ValueTimeline.MaximumSnapshotsPerValue = 3;
        for (int i = 0; i < 20; i++) { sample.Number++; watcher.SampleNow(); }
        Check(number.Timeline.GetSnapshots().Length == 3, "Snapshot retention is unbounded.");
        var types = new (object Value, ValueKind Kind)[] { (true, ValueKind.Boolean), (1d, ValueKind.Number), ("abc", ValueKind.Category), (Color.Coral, ValueKind.Color), (new Vector2(2, 3), ValueKind.Point), (new Rectangle(1, 2, 3, 4), ValueKind.Rectangle), (new Vector4(1, 2, 3, 4), ValueKind.Thickness) };
        foreach (var (value, kind) in types) Check(Visualization.Normalize(new(DateTime.UtcNow, value)).Kind == kind, "Wrong plot kind: " + kind);
        Utils.Profile = Utils.DebugProfile.NearInstant; Check(Utils.FramesPerSecond == 120 && ValueTimeline.MaximumSnapshotsPerValue == 4000, "Profile did not update retention and frequency.");
        var time = DateTime.UtcNow; var positions = Visualization.Positions([new(time, [], "", Color.White), new(time.AddSeconds(1), [], "", Color.White), new(time.AddSeconds(10), [], "", Color.White)], 0);
        Check(Math.Abs(positions[1] - .1) < .001, "Elapsed-time spacing lost a gap.");
        var compact = Visualization.Positions([new(time, [], "", Color.White), new(time.AddSeconds(1), [], "", Color.White), new(time.AddSeconds(10), [], "", Color.White)], 1);
        Check(Math.Abs(compact[1] - .5) < .001, "Gap compression was not applied.");
        DebugWatcher.MaximumMembersPerPass = 8; Utils.Profile = Utils.DebugProfile.Fast;
    }
    static void Runtime(string result)
    {
        using var app = new NativeWindow("Project-Z debugger acceptance target", 1100, 720);
        DebugWatcher? watcher = null; DebugWindow? inspector = null;
        var sample = new Sample(); int frames = 0; bool editQueued = false; Task? editTask = null;
        int stableTreeFrames = 0;
        app.Ready += () =>
        {
            app.Label("Project-Z native debugger acceptance", 30, 25, 1000, 20);
            watcher = new(sample); watcher.AttachScene(app.Scene); watcher.StartSession();
            RootDiscovery.Register(app.Scene);
        };
        app.Frame += () =>
        {
            frames++; sample.Number = 10 + Math.Sin(frames / 15d) * 5;
            if (frames == 10)
            {
                inspector = new DebugWindow(); inspector.Show();
                inspector.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Number)));
            }
            if (frames == 30 && !editQueued)
            {
                editQueued = true;
                // Exercise marshaling from a foreign thread into the scene loop.
                editTask = Task.Run(() => watcher!.Values.Single(v => v.Name == nameof(Sample.Text)).ApplyAsync("scene-thread write"));
            }
            if (frames >= 45 && frames < 69)
            {
                var capture = result + ".tree-frame.png";
                inspector!.SaveFrame(capture);
                using var image = new System.Drawing.Bitmap(capture);
                int textPixels = 0;
                for (int y = 104; y < Math.Min(image.Height - 140, 420); y += 2)
                    for (int x = 12; x < 305; x += 2)
                    {
                        var pixel = image.GetPixel(x, y);
                        if (pixel.R > 90 && pixel.G > 90 && pixel.B > 90) textPixels++;
                    }
                Check(textPixels >= 24, $"Watcher tree disappeared during refresh at frame {frames}: {textPixels} foreground pixels.");
                int bleedPixels = 0;
                for (int y = 104; y < Math.Min(image.Height - 140, 420); y++)
                    for (int x = 330; x < 345; x++)
                    {
                        var pixel = image.GetPixel(x, y);
                        if (pixel.R > 90 && pixel.G > 90 && pixel.B > 90) bleedPixels++;
                    }
                Check(bleedPixels == 0, $"Watcher text escaped into the plot gutter at frame {frames}: {bleedPixels} foreground pixels.");
                stableTreeFrames++;
            }
            if (frames == 70)
            {
                inspector!.DispatchClick(380, 75);
                Check(inspector.VisualizationMode == VisualizationMode.TimeSeries, "Native View button did not receive its click.");
            }
            if (frames == 85) inspector!.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Boolean)));
            if (frames == 95) inspector!.SaveFrame(result + ".boolean.png");
            if (frames == 100) inspector!.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Point)));
            if (frames == 105) inspector!.DispatchClick(380, 75);
            if (frames == 115) inspector!.SaveFrame(result + ".spatial.png");
            if (frames == 120) { inspector!.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Tint))); inspector.DispatchClick(380, 75); }
            if (frames == 130) inspector!.SaveFrame(result + ".events.png");
            if (frames == 135) { inspector!.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Number))); inspector.DispatchClick(380, 75); }
            if (frames > 160)
            {
                Check(inspector?.IsVisible == true, "Native debugger did not remain visible.");
                Check(stableTreeFrames == 24, "Sustained watcher tree rendering was not checked.");
                Check(editTask?.IsCompletedSuccessfully == true && sample.Text == "scene-thread write", "Scene-thread edit did not complete.");
                Check(watcher!.Values.Single(v => v.Name == nameof(Sample.Number)).Timeline.GetSnapshots().Length > 2, "Live native scene did not sample changing values.");
                Check(global::ProjectZ.Shared.Drawing.UI.SceneElement.RenderExceptionCount == 0, global::ProjectZ.Shared.Drawing.UI.SceneElement.FirstRenderException ?? "Native rendering failed.");
                var hwnd = global::ProjectZ.Windows.Composition.WindowsComposition.ResolveHwnd(app);
                Check(hwnd != IntPtr.Zero, "No real HWND was created.");
                inspector!.SaveFrame(result + ".png");
                File.WriteAllText(result, "PASS native HWND, live inspector, scene-thread sampling/editing, 24 consecutive watcher-tree frames without blanking or text bleed, rendering and continued target frames");
                inspector!.Dispose(); watcher.Dispose(); app.Exit();
            }
        };
        app.Run();
    }
    static void ProtectedRuntime(string executable, string result)
    {
        var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(executable)! };
        // An unprotected .NET host fails before Main when this hook is missing.
        start.Environment["DOTNET_STARTUP_HOOKS"] = Path.Combine(Path.GetDirectoryName(executable)!, "deliberately-missing-hook.dll");
        using var process = System.Diagnostics.Process.Start(start)!;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20); bool ready = false;
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                process.Refresh();
                try { ready = process.Modules.Cast<System.Diagnostics.ProcessModule>().Any(m => string.Equals(m.ModuleName, "ProjectZ.dll", StringComparison.OrdinalIgnoreCase)) && process.Modules.Cast<System.Diagnostics.ProcessModule>().Any(m => string.Equals(m.ModuleName, "mgruntime.dll", StringComparison.OrdinalIgnoreCase)); } catch (System.ComponentModel.Win32Exception) { }
                if (ready) break; Thread.Sleep(100);
            }
            Check(ready && !process.HasExited, "Protected Project-Z target did not load its native renderer with a blocked startup hook.");
            Thread.Sleep(1000); Check(!process.HasExited, "Protected target exited after renderer startup.");
            File.WriteAllText(result, "PASS protected Project-Z deployment runs, native renderer loads, and hostile startup-hook environment is blocked");
        }
        finally { if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(3000)) process.Kill(); } }
    }
}
