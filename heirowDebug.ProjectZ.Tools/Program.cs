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
            if (args is ["--ui-check", var checkPath]) { using var check = new ToolWindow(checkPath); check.Run(); return 0; }
            using NativeWindow app = args is ["--protect-ui"] ? new CompanionWindow() : args is ["--demo"] ? new CompanionWindow(true) : new ToolWindow();
            app.Run(); return 0;
        }
        catch (Exception ex)
        {
            if (args.LastOrDefault() is { } last && Path.IsPathRooted(last) && !Directory.Exists(last)) File.WriteAllText(last + ".error", ex.ToString());
            else System.Windows.Forms.MessageBox.Show(ex.Message, "heirowDebug Project-Z");
            return 1;
        }
    }
}

sealed class CompanionWindow : NativeWindow
{
    readonly List<SceneElement> page = new();
    TextElement status = null!;
    bool busy;
    public CompanionWindow(bool demo = false) : base("heirowDebug Project-Z tools", 1120, 760)
    {
        Ready += () =>
        {
            Label("heirowDebug / Project-Z", 28, 20, 700, 22);
            Label("Live inspection, launch-time injection and offline build protection", 30, 66, 1000, 12);
            Button("Launch + inject", 30, 108, 180, () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true }));
            Button("Protect a build", 225, 108, 180, ProtectionPage);
            Button("Live debugger demo", 420, 108, 200, Demo);
            status = Label("Ready", 30, 705, 1050, 11); if (demo) Demo(); else ProtectionPage();
        };
    }
    T OnPage<T>(T control) where T : SceneElement { page.Add(control); return control; }
    void ClearPage() { foreach (var item in page) { Scene.RemoveElement(item); item.Dispose(); } page.Clear(); }
    void Status(string text) => Pending.Enqueue(() => status.Text = text);
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
        // Keep the interactive example observable when hooks are enabled after
        // the scene callback has been JITted, matching the recording harness.
        public double Speed
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set;
        }
        public bool Running
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set;
        }
        public Microsoft.Xna.Framework.Vector2 Position
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set;
        }
        public Color Tint { get; set; } = new(84, 218, 195);
        public List<int> Values { get; set; } = [1, 2, 3];
        public string Status { get; set; } = "Project-Z native renderer";
    }
}
