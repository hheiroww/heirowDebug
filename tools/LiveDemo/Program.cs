using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using heirowDebug.WPF;

// Capture the real production debugger. Only the watched workload is a demo;
// the watcher, tree, sampling, plots, and window all come from heirowDebug.WPF.
public sealed class DemoTelemetry
{
    public double LoadPercent = 30;
    public int RequestsPerSecond = 80;
    public bool IsBusy;
    public Point CursorPosition = new(25, 30);
    public string Status = "Processing live telemetry";
}

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Specify an empty frame output directory.");
        string output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        if (Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Frame directory must be empty.");
        var app = new Application();
        var telemetry = new DemoTelemetry();
        Utils.Profile = Utils.DebugProfile.Fast;
        DebugWatcher.MaximumMembersPerPass = 8;
        var watcher = new DebugWatcher(telemetry, false, "Live application telemetry");
        DebugWatcher.CreateDebugWindow();
        var window = DebugWatcher.DebugWindow;
        window.Title = "heirowDebug 2026 · live object inspection";
        window.Width = 960;
        window.Height = 560;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        watcher.StartSession();
        var elapsed = Stopwatch.StartNew();
        var values = new List<double>();
        var selections = new List<string>();
        int frame = 0;
        string selected = "";
        var update = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        update.Tick += (_, _) =>
        {
            double t = elapsed.Elapsed.TotalSeconds;
            telemetry.LoadPercent = Math.Round(46 + 25 * Math.Sin(t * 1.7) + 8 * Math.Sin(t * 4.3), 1);
            telemetry.RequestsPerSecond = (int)(90 + 35 * Math.Cos(t * 1.4));
            telemetry.IsBusy = (int)(t * 1.2) % 2 == 0;
            telemetry.CursorPosition = new Point(Math.Round(50 + 34 * Math.Cos(t * 1.8), 1), Math.Round(50 + 25 * Math.Sin(t * 2.7), 1));
        };
        var capture = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        capture.Tick += (_, _) =>
        {
            if (elapsed.Elapsed.TotalSeconds < 3) return;
            string member = frame < 55 ? "LoadPercent" : frame < 85 ? "CursorPosition" : "IsBusy";
            var tree = (TreeView)window.FindName("Watchers");
            var root = tree.Items.OfType<TreeViewItem>().FirstOrDefault();
            if (root == null) { if (elapsed.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("Watcher tree did not populate."); return; }
            root.IsExpanded = true;
            if (selected != member)
            {
                var item = root.Items.OfType<TreeViewItem>().FirstOrDefault(i => i.Header?.ToString()?.StartsWith(member + "\r\n", StringComparison.Ordinal) == true);
                if (item == null) { if (elapsed.Elapsed.TotalSeconds > 15) throw new InvalidOperationException("Demo member not found: " + member); return; }
                item.IsSelected = true;
                selected = member;
                selections.Add(member);
                return; // Allow the normal dispatcher paint before recording.
            }
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, $"frame-{frame:0000}.png"))) encoder.Save(file);
            values.Add(telemetry.LoadPercent);
            if (++frame < 110) return;
            capture.Stop(); update.Stop();
            File.WriteAllText(Path.Combine(output, "capture.json"), JsonSerializer.Serialize(new
            {
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                processId = Environment.ProcessId,
                utc = DateTimeOffset.UtcNow,
                frames = frame,
                intervalMilliseconds = 100,
                selections,
                loadMinimum = values.Min(),
                loadMaximum = values.Max(),
                elapsedSeconds = elapsed.Elapsed.TotalSeconds
            }, new JsonSerializerOptions { WriteIndented = true }));
            window.Close();
        };
        update.Start(); capture.Start();
        app.Run();
    }
}
