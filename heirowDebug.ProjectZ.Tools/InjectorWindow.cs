using System.Security.Cryptography;
using System.Xml;
using heirowDebug.ProjectZ;
using Microsoft.Xna.Framework;
using ProjectZ.Shared.Drawing.Designer;
using ProjectZ.Shared.Drawing.UI;
using ProjectZ.Shared.Drawing.UI.Input;
using ProjectZ.Shared.Drawing.UI.Primitives;
using Button = ProjectZ.Shared.Drawing.UI.Input.Button;
using CheckBox = ProjectZ.Shared.Drawing.UI.Input.CheckBox;

namespace heirowDebug.ProjectZ.Tools;

sealed class ToolWindow : NativeWindow
{
    SceneXamlParser parser = null!;
    SceneElement root = null!;
    ProcessGrid processes = null!;
    Button refresh = null!, inject = null!;
    Textbox preferredRoot = null!;
    CheckBox recursive = null!;
    TextElement status = null!;
    bool busy;
    readonly string? checkPath;
    int frames;
    string sourceHash = "";
    public ToolWindow(string? checkPath = null) : base("heirowDebug / Project-Z", 980, 590)
    {
        this.checkPath = checkPath;
        Ready += Build;
        Frame += CheckFrame;
    }
    protected override Stream? OpenWindowIcon() => typeof(ToolWindow).Assembly.GetManifestResourceStream("Injector.ico");
    static string Resource(string name)
    {
        using var stream = typeof(ToolWindow).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    void Build()
    {
        var markup = Resource("Injector.MainWindow.xaml");
        sourceHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(markup)));
        var document = new XmlDocument(); document.LoadXml(markup);
        var app = new XmlDocument(); app.LoadXml(Resource("Injector.App.xaml"));
        var resources = document.CreateElement("Window.Resources", document.DocumentElement!.NamespaceURI);
        foreach (XmlNode child in app.SelectSingleNode("/*/*[local-name()='Application.Resources']")!.ChildNodes) resources.AppendChild(document.ImportNode(child, true));
        document.DocumentElement.PrependChild(resources);
        foreach (XmlElement node in document.SelectNodes("//*")!)
        {
            foreach (var attribute in new[] { "Text", "ToolTip" })
                if (node.HasAttribute(attribute)) node.SetAttribute(attribute, node.GetAttribute(attribute).Replace("WPF", "Project-Z"));
            if (node.LocalName == "Image" && node.HasAttribute("Source")) node.SetAttribute("Source", Path.Combine(AppContext.BaseDirectory, node.GetAttribute("Source").Replace('/', Path.DirectorySeparatorChar)));
        }
        parser = new SceneXamlParser(Scene);
        parser.RegisterFactory("DataGrid", xml => new ProcessGrid(Scene, xml));
        root = parser.Parse(document.OuterXml); Scene.AddElement(root); parser.AttachImportedLayout();
        if (parser.UnsupportedFeatures.Count != 0) throw new InvalidOperationException(string.Join(Environment.NewLine, parser.UnsupportedFeatures));
        processes = parser.FindName<ProcessGrid>("ProcessGrid");
        refresh = parser.FindName<Button>("RefreshButton"); inject = parser.FindName<Button>("InjectButton");
        preferredRoot = parser.FindName<Textbox>("RootTypeTextBox"); recursive = parser.FindName<CheckBox>("RecursiveCheckBox"); status = parser.FindName<TextElement>("StatusText");
        var placeholder = parser.FindName<TextElement>("RootTypePlaceholder");
        void UpdatePlaceholder() { placeholder.isVisible = preferredRoot.Text.Length == 0; parser.SetImportedProperty(placeholder, "Visibility", placeholder.isVisible ? "Visible" : "Collapsed"); }
        preferredRoot.OnTextChanged += _ => UpdatePlaceholder(); UpdatePlaceholder();
        Scene.BackgroundColor = new(18, 22, 26);
        refresh.MouseLeftClick += _ => { if (!busy) Refresh(); };
        inject.MouseLeftClick += _ => { if (!busy) Inject(); };
        processes.SelectionChanged += (_, _) =>
        {
            preferredRoot.Text = ""; SetBusy(busy, processes.SelectedProcess is { } p ? $"Selected {p.Name} (PID {p.Id}). Save work before restarting." : "Select a process to inject.");
        };
        if (checkPath is null) Refresh();
        else { processes.SetProcesses(Enumerable.Range(1, 40).Select(i => new ProjectZProcessInfo(1000 + i, $"Project-Z application {i:00}", $"Application window {i:00}", $"C:\\Applications\\Example{i:00}\\App.exe")).ToArray(), null); SetBusy(false, "Select a process to inject."); }
    }
    void SetBusy(bool value, string message)
    {
        busy = value; refresh.isEnabled = !value; processes.isEnabled = !value;
        preferredRoot.isEnabled = !value; recursive.isEnabled = !value;
        inject.isEnabled = !value && processes.SelectedProcess is not null;
        inject.Opacity = inject.isEnabled ? 1 : .45f; refresh.Opacity = refresh.isEnabled ? 1 : .45f;
        status.Text = message;
    }
    async void Refresh()
    {
        int? selectedId = processes.SelectedProcess?.Id;
        SetBusy(true, "Inspecting running processes for Project-Z…");
        try
        {
            var found = await Task.Run(InjectorService.GetProjectZProcesses);
            Pending.Enqueue(() => { processes.SetProcesses(found, selectedId); SetBusy(false, found.Count == 0 ? "No accessible Project-Z processes are currently running." : $"Found {found.Count} accessible Project-Z processes. Select one to inject."); });
        }
        catch (Exception ex) { Pending.Enqueue(() => SetBusy(false, ex.Message)); }
    }
    async void Inject()
    {
        if (processes.SelectedProcess is not { } target) return;
        if (System.Windows.Forms.MessageBox.Show($"Restart {target.Name} (PID {target.Id}) and inject heirowDebug?\n\nSave any work in that application before continuing. If a normal close keeps it running in the tray, a separate debug instance will be opened.", "Restart and inject", System.Windows.Forms.MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Warning) != System.Windows.Forms.DialogResult.OK) return;
        var rootType = preferredRoot.Text.Trim(); var expand = recursive.IsChecked == true;
        SetBusy(true, $"Stopping {target.Name} (PID {target.Id})…");
        string completion;
        try { completion = await InjectorService.RestartAndInjectAsync(target, expand, new Progress<string>(s => Pending.Enqueue(() => status.Text = s)), preferredRootType: rootType); }
        catch (Exception ex) { completion = "Injection failed: " + ex.Message; }
        var found = await Task.Run(InjectorService.GetProjectZProcesses);
        Pending.Enqueue(() => { processes.SetProcesses(found, target.Id); SetBusy(false, completion); });
    }
    void CheckFrame()
    {
        if (root is null) return;
        root.Size = new(Width, Height);
        if (checkPath is null) return;
        frames++;
        if (frames == 6) SaveFrame(checkPath + ".initial.png");
        if (frames == 8)
        {
            int x = (int)processes.Position.X + 15, y = (int)(processes.Position.Y + processes.HeaderHeight + processes.ItemHeight * 1.5f);
            DispatchClick(x, y);
        }
        if (frames == 10) DispatchClick((int)(recursive.Position.X + 8), (int)(recursive.Position.Y + recursive.Size.Y / 2));
        if (frames == 12)
        {
            if (processes.SelectedProcess?.Id != 1002 || !inject.isEnabled) throw new InvalidOperationException($"Clicking a process row did not enable injection: selected={processes.SelectedProcess?.Id}, enabled={inject.isEnabled}, grid={processes.Position}/{processes.Size}.");
            if (recursive.IsChecked != true) throw new InvalidOperationException("The auto-expand checkbox did not respond to its click.");
            var id = processes.SelectedProcess.Id;
            processes.SetProcesses(processes.Processes.Reverse().ToArray(), id);
            if (processes.SelectedProcess?.Id != id) throw new InvalidOperationException("Refresh lost the selected process identity.");
        }
        if (frames == 14) DispatchKey(Microsoft.Xna.Framework.Input.Keys.End);
        if (frames == 16)
        {
            if (processes.SelectedProcess?.Id != 1001) throw new InvalidOperationException("Keyboard End did not select and reveal the last process.");
            DispatchKey(Microsoft.Xna.Framework.Input.Keys.Home);
        }
        if (frames == 18)
        {
            if (processes.SelectedProcess?.Id != 1040) throw new InvalidOperationException("Keyboard Home did not select and reveal the first process.");
            processes.SetProcesses(processes.Processes.Reverse().ToArray(), 1002);
        }
        if (frames == 22) { SaveFrame(checkPath + ".png"); ResizeClient(760, 500); }
        if (frames == 28)
        {
            if (processes.ColumnCount != 4 || processes.Size.Y < 100 || Scene.Quality.AntiAliasingSamples != 16 || Scene.Quality.Sampler.MaxAnisotropy != 16) throw new InvalidOperationException("Shared layout or AA/AF validation failed.");
            if (inject.Position.X + inject.Size.X > Width || preferredRoot.Position.X + preferredRoot.Size.X > recursive.Position.X) throw new InvalidOperationException("The controls overflow at the shared XAML minimum window size.");
            SaveFrame(checkPath + ".minimum.png");
            File.WriteAllText(checkPath, System.Text.Json.JsonSerializer.Serialize(new { Result = "PASS shared WPF XAML, mouse and keyboard process selection, checkbox, scrolling, selection preservation, four columns, responsive layout, AA 16x, AF 16x", XamlSha256 = sourceHash, Scene.Quality.AntiAliasingTechnique, processes.Position, processes.Size }, new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
            Exit();
        }
    }
}
