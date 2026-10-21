using HeirowDotNetShield;
using heirowDebug.ProjectZ;
using ProjectZ.Shared.Drawing.UI;
using ProjectZ.Shared.Drawing.UI.Input;
using ProjectZ.Shared.Drawing.UI.Primitives;
using Color = Microsoft.Xna.Framework.Color;

namespace heirowDebug.ProjectZ.Tools;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args is ["--protect", var input, var output])
            {
                var result = ShieldService.ProtectAsync(new(input, output), null, CancellationToken.None).GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(output, "projectz-protection-result.txt"), result.OutputAssembly); return 0;
            }
            if (args is ["--inject-test", var pid, var resultPath])
            {
                var target = InjectorService.GetProjectZProcesses().Single(p => p.Id == int.Parse(pid, System.Globalization.CultureInfo.InvariantCulture));
                var result = InjectorService.RestartAndInjectAsync(target).GetAwaiter().GetResult(); File.WriteAllText(resultPath, result); return 0;
            }
            using var app = new ToolWindow(); app.Run(); return 0;
        }
        catch (Exception ex)
        {
            if (args.LastOrDefault() is { } last && Path.IsPathRooted(last) && !Directory.Exists(last)) File.WriteAllText(last + ".error", ex.ToString());
            else System.Windows.Forms.MessageBox.Show(ex.Message, "heirowDebug Project-Z");
            return 1;
        }
    }
}

sealed class ToolWindow : NativeWindow
{
    readonly List<SceneElement> page = new();
    TextElement status = null!;
    IReadOnlyList<ProjectZProcessInfo> processes = [];
    int selected;
    bool busy;
    bool recursive;
    public ToolWindow() : base("heirowDebug Project-Z tools", 1120, 760)
    {
        Ready += () =>
        {
            Label("heirowDebug / Project-Z", 28, 20, 700, 22);
            Label("Live inspection, launch-time injection and offline build protection", 30, 66, 1000, 12);
            Button("Launch + inject", 30, 108, 180, InjectorPage);
            Button("Protect a build", 225, 108, 180, ProtectionPage);
            Button("Live debugger demo", 420, 108, 200, Demo);
            status = Label("Ready", 30, 705, 1050, 11); InjectorPage();
        };
    }
    T OnPage<T>(T control) where T : SceneElement { page.Add(control); return control; }
    void ClearPage() { foreach (var item in page) { Scene.RemoveElement(item); item.Dispose(); } page.Clear(); }
    void Status(string text) => Pending.Enqueue(() => status.Text = text);
    void InjectorPage()
    {
        if (busy) return; ClearPage();
        OnPage(Label("Restart a running Project-Z application with the managed debugger hook", 30, 165, 1020, 15));
        OnPage(Label("Save work in the selected application before restarting it.", 30, 205, 1000));
        var list = OnPage(Label("", 30, 255, 1030, 12)); list.Size = new(1030, 230);
        var selection = OnPage(Label("No process selected", 30, 500, 1030));
        async void Refresh()
        {
            busy = true; Status("Discovering accessible Project-Z processes…");
            try
            {
                var found = await Task.Run(InjectorService.GetProjectZProcesses);
                Pending.Enqueue(() =>
                {
                    processes = found; selected = 0;
                    list.Text = string.Join(Environment.NewLine, found.Take(9).Select((p, i) => $"{i + 1}. {p.Name}  |  PID {p.Id}  |  {p.WindowTitle}"));
                    selection.Text = processes.FirstOrDefault()?.ToString() ?? "No accessible Project-Z process is running.";
                    busy = false; status.Text = $"Found {found.Count} Project-Z processes.";
                });
            }
            catch (Exception ex) { Status(ex.Message); busy = false; }
        }
        OnPage(Button("Refresh processes", 30, 550, 180, Refresh));
        OnPage(Button("Next process", 225, 550, 150, () => { if (processes.Count > 0) selection.Text = processes[selected = (selected + 1) % processes.Count].ToString(); }));
        OnPage(Button("Auto-expand objects", 390, 550, 205, () => { recursive = !recursive; status.Text = "Recursive watches: " + recursive; }));
        OnPage(Button("Restart + inject", 610, 550, 200, async () =>
        {
            if (busy || processes.Count == 0) return;
            var target = processes[selected];
            if (System.Windows.Forms.MessageBox.Show($"Restart {target.Name} (PID {target.Id}) and open heirowDebug?\n\nSave work in that application first.", "Restart + inject", System.Windows.Forms.MessageBoxButtons.OKCancel) != System.Windows.Forms.DialogResult.OK) return;
            busy = true;
            try { Status(await InjectorService.RestartAndInjectAsync(target, recursive, new Progress<string>(Status))); }
            catch (Exception ex) { Status("Injection failed: " + ex.Message); }
            finally { busy = false; }
        }));
        Refresh();
    }
    void ProtectionPage()
    {
        if (busy) return; ClearPage();
        OnPage(Label("heirowDotNetShield / Project-Z", 30, 165, 950, 17));
        OnPage(Label("Protect a copied .NET build with the same engine and native pre-runtime launcher as the WPF tool.", 30, 205, 1040));
        OnPage(Label("Application assembly (.dll)", 30, 250, 1000)); var input = OnPage(Input(30, 285, 820));
        OnPage(Button("Browse DLL", 870, 285, 180, () =>
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog { Filter = ".NET assembly (*.dll)|*.dll", CheckFileExists = true };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { input.Text = dialog.FileName; }
        }));
        OnPage(Label("Empty output directory, outside the original deployment", 30, 340, 1040)); var output = OnPage(Input(30, 375, 820));
        OnPage(Button("Choose folder", 870, 375, 180, () =>
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose an empty output directory" };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) output.Text = dialog.SelectedPath;
        }));
        var log = OnPage(Label("Obfuscate private names and strings • remove PDBs • block startup hooks and managed attach • verify SHA-256 inventory", 30, 435, 1020)); log.Size = new(1020, 210);
        OnPage(Button("Protect copied build", 30, 650, 240, async () =>
        {
            if (busy) return; busy = true;
            try
            {
                var request = new ShieldRequest(input.Text, output.Text);
                var progress = new Progress<string>(message => Pending.Enqueue(() => { log.Text += Environment.NewLine + message; status.Text = message; }));
                var result = await ShieldService.ProtectAsync(request, progress, CancellationToken.None);
                Status("Protected build verified: " + result.OutputAssembly);
            }
            catch (Exception ex) { Status("Protection failed: " + ex.Message); }
            finally { busy = false; }
        }));
    }
    readonly Telemetry telemetry = new();
    DebugWatcher? demoWatcher;
    DebugWindow? inspector;
    void Demo()
    {
        if (inspector?.IsVisible == true) return;
        demoWatcher ??= new DebugWatcher(telemetry, false, "Project-Z demo telemetry");
        demoWatcher.AttachScene(Scene); demoWatcher.StartSession();
        if (inspector is null)
        {
            Scene.FrameUpdated += time =>
            {
                telemetry.Speed = 60 + Math.Sin(time.TotalGameTime.TotalSeconds) * 35;
                telemetry.Position = new((float)(150 + Math.Cos(time.TotalGameTime.TotalSeconds) * 90), (float)(150 + Math.Sin(time.TotalGameTime.TotalSeconds) * 90));
                telemetry.Running = (int)time.TotalGameTime.TotalSeconds % 2 == 0;
            };
            inspector = new DebugWindow();
        }
        inspector.Show(); inspector.Select(demoWatcher, demoWatcher.Values.Single(v => v.Name == nameof(Telemetry.Speed)));
    }
    protected override void Dispose(bool disposing) { if (disposing) { inspector?.Dispose(); demoWatcher?.Dispose(); } base.Dispose(disposing); }
    sealed class Telemetry
    {
        public double Speed { get; set; }
        public bool Running { get; set; }
        public Microsoft.Xna.Framework.Vector2 Position { get; set; }
        public Color Tint { get; set; } = new(84, 218, 195);
        public List<int> Values { get; set; } = [1, 2, 3];
        public string Status { get; set; } = "Project-Z native renderer";
    }
}
