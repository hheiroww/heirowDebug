using System.Collections;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ProjectZ.Shared.Drawing;
using ProjectZ.Shared.Drawing.UI;
using ProjectZ.Shared.Drawing.UI.Input;
using ProjectZ.Shared.Drawing.UI.Primitives;

namespace heirowDebug.ProjectZ;

/// <summary>Native scene inspector; no WPF windows or MahApps controls are used.</summary>
public sealed class DebugWindow : IDisposable
{
    NativeWindow? window;
    Thread? thread;
    Scene? hostScene;
    Scene.FrameUpdatedEventHandler? pump;
    volatile bool closeRequested;
    bool pumping;
    readonly List<TextElement> rows = new();
    readonly List<(DebugWatcher Owner, DebugValue? Value)> rowTargets = new();
    readonly HashSet<string> expanded = new();
    readonly List<global::ProjectZ.Shared.Drawing.UI.Input.Button> actionButtons = new();
    DebugValue? selected;
    DebugWatcher? selectedOwner;
    Textbox edit = null!;
    Textbox filter = null!;
    TextElement title = null!, summary = null!, status = null!, events = null!;
    HistoryPlot plot = null!;
    Trackbar lower = null!, upper = null!, compression = null!;
    readonly List<SceneElement> targetControls = new();
    readonly List<WatchTarget> targets = new();
    int targetIndex;
    int scroll;
    int lastWidth;
    int lastHeight;
    long nextRefresh;
    bool backing;
    bool enabled = true;
    public bool Enabled { get => enabled; set { enabled = value; foreach (var watcher in DebugWatcher.Watchers.Values) watcher.isEnabled = value; } }
    public string Title { get; set; } = $"heirowDebug Project-Z – {System.Diagnostics.Process.GetCurrentProcess().ProcessName} (PID {Environment.ProcessId})";
    public bool IsVisible => window is { Closed: false, IsWindowVisible: true };
    public IntPtr Handle => window is null || window.Closed ? IntPtr.Zero : global::ProjectZ.Windows.Composition.WindowsComposition.ResolveHwnd(window);
    public DebugValue? SelectedValue => selected;
    public void SaveFrame(string path) => (window ?? throw new InvalidOperationException("Open the debugger first.")).SaveFrame(path);
    public void DispatchClick(int x, int y) => (window ?? throw new InvalidOperationException("Open the debugger first.")).DispatchClick(x, y);
    public VisualizationMode VisualizationMode => plot.Mode;
    public void Show()
    {
        if (IsVisible || thread is { IsAlive: true }) return;
        closeRequested = false;
        hostScene = DebugWatcher.Watchers.Values.Select(w => w.OwnerScene).FirstOrDefault(s => s is not null);
        if (hostScene is not null)
        {
            window = MakeWindow(); window.RunOneFrame(); window.ShowNativeWindow();
            pump = _ =>
            {
                if (pumping) return; pumping = true;
                try { if (closeRequested || window.Closed) DisposeSurface(); else window.RunOneFrame(); }
                finally { pumping = false; }
            };
            hostScene.FrameUpdated += pump;
        }
        else
        {
            thread = new Thread(() => { using var surface = MakeWindow(); window = surface; surface.Run(); }) { Name = "heirowDebug Project-Z", IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
    }
    NativeWindow MakeWindow()
    {
        var surface = new NativeWindow(Title);
        surface.Ready += () => { window = surface; Build(); };
        surface.Frame += () =>
        {
            if (closeRequested) surface.Exit();
            if (lastWidth != surface.Width || lastHeight != surface.Height) Layout();
            if (Environment.TickCount64 >= nextRefresh) { RefreshRows(); nextRefresh = Environment.TickCount64 + 250; }
            summary.Text = plot.Summary; events.Text = plot.EventText;
            events.isVisible = plot.Mode == VisualizationMode.Events || Visualization.Normalize(new(DateTime.UtcNow, selected?.Value)).Kind == ValueKind.Text;
        };
        return surface;
    }
    void Build()
    {
        var w = window!;
        title = w.Label("Live visualization", 350, 20, 700, 17);
        summary = w.Label("Choose a value", 350, 49, 800, 10);
        w.Button("Debugging on/off", 12, 16, 150, () => { Enabled = !Enabled; status.Text = Enabled ? "Sampling enabled" : "Sampling paused"; });
        w.Button("+ Watch…", 172, 16, 145, ToggleTargets);
        filter = w.Input(12, 58, 305);
        filter.OnTextChanged += _ => { scroll = 0; RefreshRows(); };
        plot = w.Add(new HistoryPlot(w.Scene) { Position = new(345, 94), Size = new(900, 500) });
        events = w.Label("", 365, 114, 800); events.Size = new(800, 440);
        edit = w.Input(345, 650, 300);
        actionButtons.Add(w.Button("Apply live value", 655, 650, 150, async () =>
        {
            if (selected is null) return;
            try { await selected.ApplyAsync(edit.Text, backing); w.Pending.Enqueue(() => status.Text = "Applied to the live object."); }
            catch (Exception ex) { w.Pending.Enqueue(() => status.Text = ex.Message); }
        }));
        actionButtons.Add(w.Button("Also set _field", 815, 650, 155, () => { backing = !backing; status.Text = "Also set backing field: " + backing; }));
        actionButtons.Add(w.Button("Add Watch", 980, 650, 130, () => { if (selected?.Value is { } obj && !obj.GetType().IsValueType && obj is not string) { var watcher = DebugWatcher.WatchStaticObject(obj, selected.Name); if (selectedOwner?.OwnerScene is { } scene) watcher.AttachScene(scene); watcher.StartSession(); RefreshRows(); } }));
        lower = Range(345, 700, 365, 0); upper = Range(735, 700, 365, 100);
        lower.ValueChanged += n => { if (lower.Value >= upper.Value) lower.Value = Math.Max(0, upper.Value - 1); plot.Lower = lower.Value / 100d; };
        upper.ValueChanged += n => { if (upper.Value <= lower.Value) upper.Value = Math.Min(100, lower.Value + 1); plot.Upper = upper.Value / 100d; };
        w.Button("View", 345, 60, 90, () => { plot.Mode = (VisualizationMode)(((int)plot.Mode + 1) % 4); status.Text = "View: " + plot.Mode; });
        w.Button("Invert spatial", 445, 60, 125, () => plot.Invert = !plot.Invert);
        w.Label("Compact gaps", 585, 65, 115, 10);
        compression = Range(705, 64, 185, 0);
        compression.ValueChanged += n => plot.Compression = compression.Value / 100d;
        w.Button("Sampling profile", 905, 60, 155, () => { Utils.Profile = (Utils.DebugProfile)(((int)Utils.Profile + 1) % 5); status.Text = $"{Utils.Profile}: {Utils.FramesPerSecond} Hz"; });
        status = w.Label("Select a value. Expand objects on demand. Drag the divider to resize the tree.", 12, 752, 1180, 10);
        w.Scene.OnMouseWheel += (delta, point) => { if (point.X < treeWidth) { scroll = Math.Max(0, scroll - delta / 120 * 3); RefreshRows(); } };
        w.Scene.OnMouseDrag += (point, start) => { if (Math.Abs(start.X - treeWidth) < 8) { treeWidth = Math.Clamp(point.X, 230, Math.Min(650, w.Width - 400)); Layout(); } };
        w.Scene.OnKeyDown += (key, keyboard) =>
        {
            if (key == Keys.Escape && targetControls.Count > 0) HideTargets();
            if (key == Keys.Down && !edit.isSelected && !filter.isSelected) SelectRelative(1);
            if (key == Keys.Up && !edit.isSelected && !filter.isSelected) SelectRelative(-1);
        };
        Layout(); RefreshRows();
    }
    Trackbar Range(float x, float y, float width, double value)
    {
        var control = new Trackbar(window!.Scene) { Position = new(x, y), Size = new(width, 22), MinimumValue = 0, MaximumValue = 100, Value = value, ShowValue = false, FixedInterval = false };
        return window.Add(control);
    }
    int treeWidth = 330;
    void Layout()
    {
        var w = window!; lastWidth = w.Width; lastHeight = w.Height;
        float left = treeWidth + 15, width = Math.Max(200, w.Width - left - 20);
        plot.Position = new(left, 102); plot.Size = new(width, Math.Max(100, w.Height - 282));
        title.Position = new(left, 16); summary.Position = new(left, 44);
        title.Size = new(width, 32); summary.Size = new(width, 28);
        edit.Position = new(left, w.Height - 163); edit.Size = new(width, 30);
        float actionX = left;
        foreach (var button in actionButtons) { button.Position = new(actionX, w.Height - 120); button.Size = new(Math.Min(button.Size.X, width / 3 - 8), 30); actionX += button.Size.X + 10; }
        lower.Position = new(left, w.Height - 63); upper.Position = new(left + width / 2, w.Height - 63);
        lower.Size = upper.Size = new(width / 2 - 10, 22);
        events.Position = new(left + 15, 120); events.Size = new(width - 30, Math.Max(80, w.Height - 282));
        status.Position = new(12, w.Height - 28); status.Size = new(w.Width - 24, 24);
        RefreshRows();
    }
    void SelectRelative(int step)
    {
        var values = rowTargets.Where(r => r.Value is not null).ToArray();
        if (values.Length == 0) return;
        var index = Array.FindIndex(values, v => ReferenceEquals(v.Value, selected));
        var next = values[Math.Clamp(index + step, 0, values.Length - 1)]; Select(next.Owner, next.Value!);
    }
    public void Select(DebugWatcher owner, DebugValue value)
    {
        selectedOwner = owner; selected = value;
        if (window is null) return;
        plot.Timeline = value.Timeline; title.Text = owner.Name + "." + value.Name;
        edit.Text = ValueTimeline.Format(value.Value); status.Text = value.Error ?? (value.CanWrite ? "Live editing available" : "Read-only value");
    }
    void RefreshRows()
    {
        if (window is null || filter is null) return;
        rowTargets.Clear();
        var all = new List<(DebugWatcher Owner, DebugValue? Value, int Depth, string Text)>();
        foreach (var watcher in DebugWatcher.Watchers.Values.Where(w => !w.isChild)) AppendWatcher(watcher);
        void AppendWatcher(DebugWatcher watcher)
        {
            if (!watcher.isChild) all.Add((watcher, null, 0, watcher.Name));
            foreach (var value in watcher.Values)
            {
                if (string.IsNullOrWhiteSpace(filter.Text) || (value.Name + " " + watcher.Name).Contains(filter.Text, StringComparison.OrdinalIgnoreCase))
                    all.Add((watcher, value, watcher.Depth + 1, $"{value.Name}: {value.Error ?? ValueTimeline.Format(value.Value)}"));
                if (expanded.Contains(value.Guid)) foreach (var child in watcher.Children.Where(c => c.ParentValueGuid == value.Guid)) AppendWatcher(child);
            }
        }
        scroll = Math.Clamp(scroll, 0, Math.Max(0, all.Count - 1));
        int available = Math.Max(1, (window.Height - 152) / 25);
        var visible = all.Skip(scroll).Take(available).ToArray();
        for (int rowIndex = 0; rowIndex < visible.Length; rowIndex++)
        {
            var entry = visible[rowIndex];
            if (rowIndex == rows.Count)
            {
                int index = rowIndex;
                var created = new TextElement(window.Scene) { Font = global::ProjectZ.Shared.Content.Fonts.SegoeUI.get_GetResourceName(10), Clip = true };
                created.MouseLeftClick += p =>
                {
                    if (index >= rowTargets.Count || rowTargets[index].Value is not { } value) return;
                    var owner = rowTargets[index].Owner;
                    Select(owner, value);
                    // Mouse click coordinates are local to this row.
                    if (p.X < 18)
                    {
                        try { if (!expanded.Remove(value.Guid) && owner.Expand(value) is not null) expanded.Add(value.Guid); } catch (Exception ex) { status.Text = ex.Message; }
                        RefreshRows();
                    }
                };
                rows.Add(window.Add(created));
            }
            var row = rows[rowIndex];
            float x = 12 + Math.Min(entry.Depth, 4) * 12, y = 102 + rowIndex * 25;
            var text = entry.Value is null ? entry.Text : "+ " + entry.Text;
            if (row.Text != text) row.Text = text;
            row.Position = new(x, y); row.Size = new(treeWidth - x - 6, 24);
            row.ForegroundColor = entry.Value == selected ? new Color(84, 218, 195) : entry.Value is null ? Color.White : new Color(183, 199, 211);
            row.isVisible = true; rowTargets.Add((entry.Owner, entry.Value));
        }
        // Keep scene registrations alive across periodic updates. Removing and
        // re-adding after Scene.Tick left the next rendered frame without rows.
        for (int index = visible.Length; index < rows.Count; index++) rows[index].isVisible = false;
    }
    sealed record WatchTarget(string Name, Func<object?> Resolve, Type? StaticType = null);
    void ToggleTargets()
    {
        if (targetControls.Count > 0) { HideTargets(); return; }
        var w = window!;
        var panel = w.Add(new RectangleElement(w.Scene) { Position = new(20, 100), Size = new(610, 230), BackgroundColor = new Color(34, 46, 59), zIndex = 500 }); targetControls.Add(panel);
        var search = w.Input(35, 145, 570); search.zIndex = 501; targetControls.Add(search);
        var heading = w.Label("Watch a scene, loaded type or static object", 35, 110, 570, 13); heading.zIndex = 501; targetControls.Add(heading);
        var choice = w.Label("", 35, 190, 570); choice.zIndex = 501; targetControls.Add(choice);
        void Refresh()
        {
            targets.Clear(); targetIndex = 0;
            foreach (var root in RootDiscovery.FindRoots()) targets.Add(new(root.GetType().FullName ?? root.GetType().Name, () => root));
            foreach (var type in RootDiscovery.ApplicationTypes())
            {
                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var fields = type.GetFields(flags); var properties = type.GetProperties(flags).Where(p => p.GetMethod is not null && p.GetIndexParameters().Length == 0);
                if (fields.Length > 0 || properties.Any()) targets.Add(new("[Static type] " + type.FullName, () => null, type));
                foreach (var field in fields.Where(f => !f.FieldType.IsValueType && f.FieldType != typeof(string))) targets.Add(new(type.FullName + "." + field.Name, () => field.GetValue(null)));
                foreach (var property in properties.Where(p => !p.PropertyType.IsValueType && p.PropertyType != typeof(string))) targets.Add(new(type.FullName + "." + property.Name, () => property.GetValue(null)));
            }
            targets.RemoveAll(t => !t.Name.Contains(search.Text, StringComparison.OrdinalIgnoreCase)); choice.Text = targets.FirstOrDefault()?.Name ?? "No matching targets";
        }
        search.OnTextChanged += _ => Refresh();
        var next = w.Button("Next target", 35, 240, 125, () => { if (targets.Count > 0) choice.Text = targets[targetIndex = (targetIndex + 1) % targets.Count].Name; }); next.zIndex = 501; targetControls.Add(next);
        var add = w.Button("Add selected", 170, 240, 140, () =>
        {
            if (targets.Count == 0) return;
            try
            {
                var target = targets[targetIndex]; DebugWatcher watcher;
                if (target.StaticType is { } type) watcher = DebugWatcher.WatchStaticType(type);
                else { var instance = target.Resolve(); if (instance is null) { choice.Text = "Selected member is null; no watcher added."; return; } watcher = DebugWatcher.WatchStaticObject(instance, target.Name); }
                if (hostScene is not null) watcher.AttachScene(hostScene); watcher.StartSession(); RefreshRows(); HideTargets();
            }
            catch (Exception ex) { choice.Text = (ex.InnerException ?? ex).Message; }
        }); add.zIndex = 501; targetControls.Add(add);
        var all = w.Button("Watch all roots", 320, 240, 145, () => { foreach (var root in RootDiscovery.FindRoots()) { try { var watcher = DebugWatcher.WatchWindow(root); watcher.StartSession(); } catch (InvalidOperationException ex) { status.Text = ex.Message; break; } } RefreshRows(); }); all.zIndex = 501; targetControls.Add(all);
        var close = w.Button("Close", 475, 240, 110, HideTargets); close.zIndex = 501; targetControls.Add(close);
        Refresh();
    }
    void HideTargets() { foreach (var control in targetControls) { window!.Scene.RemoveElement(control); control.Dispose(); } targetControls.Clear(); }
    public void Close() { closeRequested = true; window?.Pending.Enqueue(() => window.Exit()); }
    void DisposeSurface()
    {
        if (pump is not null && hostScene is not null) hostScene.FrameUpdated -= pump;
        pump = null; window?.Dispose(); window = null;
        rows.Clear(); rowTargets.Clear(); actionButtons.Clear(); targetControls.Clear();
    }
    public void Dispose() { Close(); if (hostScene is null) thread?.Join(TimeSpan.FromSeconds(3)); else DisposeSurface(); }
}

public static class RootDiscovery
{
    static readonly HashSet<object> registered = new(ReferenceEqualityComparer.Instance);
    public static void Register(object instance) { lock (registered) registered.Add(instance); }
    public static void Unregister(object instance) { lock (registered) registered.Remove(instance); }
    public static IEnumerable<Type> ApplicationTypes()
    {
        var entry = Assembly.GetEntryAssembly();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a == entry ? 0 : 1))
        {
            var name = assembly.GetName().Name ?? "";
            if (name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft", StringComparison.Ordinal) || name.StartsWith("heirowDebug", StringComparison.Ordinal) || name.StartsWith("ProjectZ", StringComparison.Ordinal) || name.StartsWith("MonoGame", StringComparison.Ordinal) || assembly.IsDynamic) continue;
            Type[] types;
            try { types = assembly.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); } catch { continue; }
            foreach (var type in types.Where(t => !t.ContainsGenericParameters)) yield return type;
        }
    }
    public static object[] FindRoots()
    {
        var roots = new HashSet<object>(ReferenceEqualityComparer.Instance); lock (registered) roots.UnionWith(registered);
        // Read MonoGame's live game/window fields before the application type
        // budget. Large applications can otherwise exhaust that budget before
        // reaching Game._instance, especially when Main only holds a local game.
        ReadFields(typeof(Game));
        foreach (var type in typeof(Game).Assembly.GetTypes().Where(t => t.Name.Contains("GameWindow", StringComparison.Ordinal) || t.Name.Contains("GamePlatform", StringComparison.Ordinal))) ReadFields(type);
        // A live framework root already owns its scenes. Avoid initializing
        // thousands of unrelated application statics just to rediscover it.
        if (roots.Any(value => value is Game or Scene or SceneManager)) return roots.Take(DebugWatcher.MaximumWatcherCount).ToArray();
        foreach (var type in ApplicationTypes().Take(4096)) ReadFields(type);
        return roots.Take(DebugWatcher.MaximumWatcherCount).ToArray();
        void ReadFields(Type type)
        {
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Take(192))
                try { Add(field.GetValue(null), 0); } catch { }
        }
        void Add(object? value, int depth)
        {
            if (value is null || depth > 4 || roots.Count >= DebugWatcher.MaximumWatcherCount) return;
            if (value is Scene or SceneManager or Game) { roots.Add(value); return; }
            if (value is IDictionary dictionary) { int n = 0; foreach (DictionaryEntry item in dictionary) { if (++n > 64) break; Add(item.Value, depth + 1); } }
            else if (value is IEnumerable items && value is not string) { int n = 0; foreach (var item in items) { if (++n > 64) break; Add(item, depth + 1); } }
            else if (value.GetType().Assembly == typeof(Game).Assembly && (value is GameWindow || value.GetType().Name.Contains("GamePlatform", StringComparison.Ordinal)))
                for (var type = value.GetType(); type is not null; type = type.BaseType)
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Take(192))
                        if (!field.FieldType.IsValueType && field.FieldType != typeof(string)) try { Add(field.GetValue(value), depth + 1); } catch { }
        }
    }
    public static Scene? FindScene(object root)
    {
        if (root is Scene scene) return scene;
        if (root is SceneManager manager) return manager.ActiveScene;
        // GetFields on a derived type omits private fields declared by its bases.
        int remaining = 192;
        for (var type = root.GetType(); type is not null && remaining > 0; type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (--remaining < 0) break;
                try
                {
                    var value = field.GetValue(root);
                    if (value is Scene found) return found;
                    if (value is SceneManager owner && owner.ActiveScene is { } active) return active;
                }
                catch { }
            }
        return null;
    }
}
