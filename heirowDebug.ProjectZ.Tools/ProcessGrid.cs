using System.Globalization;
using System.Xml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using ProjectZ.Shared.Content;
using ProjectZ.Shared.Drawing;
using ProjectZ.Shared.Drawing.UI;
using ProjectZ.Shared.Drawing.UI.Input;
using ProjectZ.Shared.Drawing.UI.Primitives;
using ListBox = ProjectZ.Shared.Drawing.UI.Input.ListBox;
using Keys = Microsoft.Xna.Framework.Input.Keys;

namespace heirowDebug.ProjectZ.Tools;

// Native DataGrid adapter for the shared WPF injector XAML. Column labels,
// bindings and widths come from that document, rather than a second layout.
sealed class ProcessGrid : ListBox
{
    readonly Scene scene;
    readonly CollectionScrollBar scrollBar;
    readonly Dictionary<int, SceneElement> rows = new();
    bool syncingScroll;
    readonly (string Header, string Binding, float Width)[] columns;
    public IReadOnlyList<ProjectZProcessInfo> Processes { get; private set; } = [];
    public ProjectZProcessInfo? SelectedProcess => SelectedIndex >= 0 && SelectedIndex < Processes.Count ? Processes[SelectedIndex] : null;
    public int ColumnCount => columns.Length;
    public ProcessGrid(Scene scene, XmlElement markup) : base(scene)
    {
        this.scene = scene;
        columns = markup.SelectNodes("*[local-name()='DataGrid.Columns']/*")!.OfType<XmlElement>().Select(column => (
            column.GetAttribute("Header"), column.GetAttribute("Binding").Replace("{Binding ", "").TrimEnd('}'),
            float.TryParse(column.GetAttribute("Width"), CultureInfo.InvariantCulture, out var width) ? width : 0)).ToArray();
        Padding = new Thickness(0); ItemHeight = 30; HeaderHeight = 30; SelectionMode = 0; VirtualizeItemTemplates = false;
        BackgroundColor = new(23, 27, 32); BorderColor = new(59, 68, 77); BorderThickness = 1;
        SelectionColor = new(39, 86, 73); TabStop = true; CanSelect = true;
        HeaderVisual = CreateRow(-1);
        HeaderVisual.zIndex = 1000;
        scrollBar = new CollectionScrollBar(scene) { zIndex = 1001, AccentColor = new(113, 128, 121), OrientationReserve = DisplayReservation.FloatBoth };
        Children.Add(scrollBar);
        scrollBar.OffsetChanged += first =>
        {
            if (syncingScroll || Processes.Count == 0) return;
            ScrollIntoView(Math.Min(Processes.Count - 1, first + scrollBar.VisibleItems - 1));
            ScrollIntoView(first);
        };
        ImportedLayout = () =>
        {
            base.AlignChildren();
            scrollBar.ItemCount = Processes.Count;
            scrollBar.VisibleItems = Math.Max(1, (int)((Size.Y - HeaderHeight) / ItemHeight));
            scrollBar.isVisible = scrollBar.ItemCount > scrollBar.VisibleItems;
            scrollBar.Size = new(12, Math.Max(1, Size.Y - HeaderHeight));
            scrollBar.Position = Position + new Vector2(Size.X - 12, HeaderHeight);
            if (rows.TryGetValue(0, out var firstRow))
            {
                syncingScroll = true;
                try { scrollBar.FirstIndex = Math.Max(0, (int)Math.Round((Position.Y + HeaderHeight - firstRow.Position.Y) / ItemHeight)); }
                finally { syncingScroll = false; }
            }
        };
        ItemVisualFactory = CreateRow;
        SelectionChanged += (_, index) => { if (index >= 0) { scene.FocusElement(this); ScrollIntoView(index); } };
        OnKeyDown += (key, _) =>
        {
            if (!isEnabled || Processes.Count == 0) return;
            int next = key switch { Keys.Down => Math.Min(Processes.Count - 1, SelectedIndex + 1), Keys.Up => Math.Max(0, SelectedIndex - 1), Keys.Home => 0, Keys.End => Processes.Count - 1, _ => SelectedIndex };
            if (next != SelectedIndex) SelectedIndex = next;
        };
    }
    public void SetProcesses(IReadOnlyList<ProjectZProcessInfo> processes, int? selectedId)
    {
        Processes = processes;
        rows.Clear();
        Items = processes.Select(p => p.Name).ToList();
        // The framework version supported by this package registers descendants
        // when attached; register rows created by a later process refresh too.
        foreach (var child in Children) { child.Padding = new Thickness(0); scene.AddElement(child); }
        SelectedIndex = selectedId is null ? -1 : processes.ToList().FindIndex(p => p.Id == selectedId);
    }
    SceneElement CreateRow(int index)
    {
        var row = new RectangleElement(scene) { Size = new(Math.Max(1, Size.X), 30), Padding = new Thickness(0), isMouseBypassEnabled = true, Clip = true };
        if (index >= 0) rows[index] = row;
        var cells = columns.Select(column => new TextElement(scene)
        {
            Text = index < 0 ? column.Header : Value(Processes[index], column.Binding),
            ForegroundColor = new(241, 245, 243), Font = Fonts.SegoeUI.get_GetResourceName(12), FontScale = .75f,
            isMouseBypassEnabled = true, Clip = true, OrientationReserve = DisplayReservation.FloatBoth
        }).ToArray();
        foreach (var cell in cells) row.Children.Add(cell);
        row.ImportedLayout = () =>
        {
            row.BackgroundColor = index < 0 ? new(32, 37, 43) : index == SelectedIndex ? SelectionColor : index % 2 == 0 ? new(23, 27, 32) : new(29, 35, 41);
            row.Size = new(Math.Max(1, Size.X), 30);
            float x = 0, fixedWidth = columns.Sum(c => c.Width);
            for (int i = 0; i < cells.Length; i++)
            {
                var width = columns[i].Width > 0 ? columns[i].Width : Math.Max(80, Size.X - fixedWidth - (scrollBar?.isVisible == true ? 14 : 0));
                cells[i].Position = row.Position + new Vector2(x + 8, 5);
                cells[i].Size = new(Math.Max(1, width - 12), 22);
                x += width;
            }
        };
        return row;
    }
    static string Value(ProjectZProcessInfo process, string binding) => binding switch
    {
        "Name" => process.Name, "Id" => process.Id.ToString(CultureInfo.InvariantCulture),
        "WindowTitle" => process.WindowTitle, "ExecutablePath" => process.ExecutablePath, _ => ""
    };
}
