using System.Globalization;
using System.Xml;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ProjectZ.Shared.Drawing;
using ProjectZ.Shared.Drawing.UI;

namespace heirowDebug.ProjectZ;

// Native adapter for the actual mah:RangeSlider declaration in DebugWindow.xaml.
// One shared track supports either endpoint and dragging the selected interval.
internal sealed class HistoryRangeSlider : SceneElement
{
    readonly Scene scene;
    Texture2D? pixel;
    double minimum, maximum = 100, lower, upper = 100;
    double dragStart, dragLower, dragUpper;
    int activeThumb;
    bool dragging;
    public event Action? RangeChanged;
    public double MinRange { get; set; }
    public double MinRangeWidth { get; set; }
    public double SmallChange { get; set; } = 1;
    public double Minimum { get => minimum; set { minimum = value; maximum = Math.Max(maximum, value); SetRange(lower, upper); } }
    public double Maximum { get => maximum; set { maximum = Math.Max(minimum, value); SetRange(lower, upper); } }
    public double LowerValue { get => lower; set => SetRange(Math.Min(value, upper - MinRange), upper); }
    public double UpperValue { get => upper; set => SetRange(lower, Math.Max(value, lower + MinRange)); }
    float TrackWidth => Math.Max(1, Size.X - 20);
    double ValueAt(float x) => minimum + Math.Clamp((x - Position.X - 10) / TrackWidth, 0, 1) * (maximum - minimum);
    float X(double value) => Position.X + 10 + (float)((value - minimum) / Math.Max(double.Epsilon, maximum - minimum)) * TrackWidth;

    public HistoryRangeSlider(Scene scene, XmlElement markup) : base(scene)
    {
        this.scene = scene; CanSelect = true; TabStop = true;
        double Read(string name, double fallback) => double.TryParse(markup.GetAttribute(name), CultureInfo.InvariantCulture, out double value) ? value : fallback;
        minimum = Read("Minimum", 0); maximum = Read("Maximum", 100);
        lower = Read("LowerValue", minimum); upper = Read("UpperValue", maximum);
        MinRange = Read("MinRange", 0); MinRangeWidth = Read("MinRangeWidth", 0); SmallChange = Read("SmallChange", 1);
        MouseLeftDown += point =>
        {
            scene.FocusElement(this); dragging = true;
            float x = Position.X + point.X;
            activeThumb = x > X(lower) + 9 && x < X(upper) - 9 ? 2 : Math.Abs(x - X(lower)) <= Math.Abs(x - X(upper)) ? 0 : 1;
            dragStart = ValueAt(x); dragLower = lower; dragUpper = upper;
            MovePointer(x);
        };
        scene.OnMouseMove += Move;
        scene.OnMouseLeftUp += Release;
        OnKeyDown += (key, keyboard) =>
        {
            if (!isEnabled) return;
            if (key == Keys.Space) { activeThumb = activeThumb == 0 ? 1 : 0; return; }
            double delta = key is Keys.Left or Keys.Down ? -SmallChange : key is Keys.Right or Keys.Up ? SmallChange : 0;
            if (keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl))
            {
                delta = Math.Clamp(delta, minimum - lower, maximum - upper); SetRange(lower + delta, upper + delta);
            }
            else if (activeThumb == 1)
                UpperValue = key == Keys.Home ? lower + MinRange : key == Keys.End ? maximum : upper + delta;
            else LowerValue = key == Keys.Home ? minimum : key == Keys.End ? upper - MinRange : lower + delta;
        };
    }
    public void SetRange(double start, double end)
    {
        if (!double.IsFinite(start) || !double.IsFinite(end)) return;
        start = Math.Clamp(start, minimum, maximum);
        end = Math.Clamp(end, Math.Min(maximum, start + MinRange), maximum);
        if (lower == start && upper == end) return;
        lower = start; upper = end; RangeChanged?.Invoke();
    }
    void Move(Point current, Point previous) { if (dragging && isMouseDown) MovePointer(current.X); }
    void Release(Point point) => dragging = false;
    void MovePointer(float x)
    {
        if (activeThumb == 2)
        {
            double delta = Math.Clamp(ValueAt(x) - dragStart, minimum - dragLower, maximum - dragUpper);
            SetRange(dragLower + delta, dragUpper + delta);
        }
        else if (activeThumb == 0) LowerValue = ValueAt(x);
        else UpperValue = ValueAt(x);
    }
    protected override void Draw(GameTime time)
    {
        if (pixel is null) { pixel = new(spriteBatch.GraphicsDevice, 1, 1); pixel.SetData(new[] { Color.White }); }
        void Fill(float x, float y, float width, float height, Color color) => spriteBatch.Draw(pixel, new Rectangle((int)x, (int)y, Math.Max(1, (int)width), Math.Max(1, (int)height)), color);
        float y = Position.Y + Size.Y / 2;
        Fill(Position.X + 10, y - 3, TrackWidth, 6, new(57, 69, 80));
        Fill(X(lower), y - 3, Math.Max(1, X(upper) - X(lower)), 6, new(58, 141, 130));
        for (int i = 0; i < 2; i++)
        {
            float x = X(i == 0 ? lower : upper);
            Fill(x - 6, y - 13, 12, 26, isSelected && activeThumb == i ? new(103, 237, 214) : new(180, 199, 209));
            Fill(x - 1, y - 7, 2, 14, new(44, 61, 72));
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { scene.OnMouseMove -= Move; scene.OnMouseLeftUp -= Release; pixel?.Dispose(); }
        base.Dispose(disposing);
    }
}
