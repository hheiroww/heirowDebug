using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectZ.Shared.Drawing;
using ProjectZ.Shared.Drawing.UI;
using ProjectZ.Shared.Drawing.UI.Primitives;
using ProjectZ.Shared.Content;

namespace heirowDebug.ProjectZ;

public enum VisualizationMode { Auto, TimeSeries, SpatialOverlay, Events }
public enum ValueKind { Number, Boolean, Category, Color, Point, Rectangle, Thickness, Text }
public sealed record PlotSample(DateTime Timestamp, double[] Channels, string Text, Color Color);

/// <summary>Normalizes both XNA/native and ordinary .NET geometry without a WPF UI dependency.</summary>
public static class Visualization
{
    public static (ValueKind Kind, PlotSample Sample) Normalize(ValueSnapshot snapshot)
    {
        var value = snapshot.Value; var text = ValueTimeline.Format(value);
        PlotSample Make(params double[] channels) => new(snapshot.Timestamp, channels, text, Color.Cyan);
        if (value is bool boolean) return (ValueKind.Boolean, Make(boolean ? 1 : 0));
        if (value is Color color) return (ValueKind.Color, new(snapshot.Timestamp, [color.R, color.G, color.B, color.A], text, color));
        if (value is System.Drawing.Color drawing) return (ValueKind.Color, new(snapshot.Timestamp, [drawing.R, drawing.G, drawing.B, drawing.A], text, new Color(drawing.R, drawing.G, drawing.B, drawing.A)));
        if (value is Vector2 vector) return (ValueKind.Point, Make(vector.X, vector.Y));
        if (value is Vector3 vector3) return (ValueKind.Point, Make(vector3.X, vector3.Y, vector3.Z));
        if (value is Vector4 vector4) return (ValueKind.Thickness, Make(vector4.X, vector4.Y, vector4.Z, vector4.W));
        if (value is Point point) return (ValueKind.Point, Make(point.X, point.Y));
        if (value is Rectangle rect) return (ValueKind.Rectangle, Make(rect.X, rect.Y, rect.Width, rect.Height));
        if (value is System.Drawing.Point dp) return (ValueKind.Point, Make(dp.X, dp.Y));
        if (value is System.Drawing.PointF dpf) return (ValueKind.Point, Make(dpf.X, dpf.Y));
        if (value is System.Drawing.Size size) return (ValueKind.Point, Make(size.Width, size.Height));
        if (value is System.Drawing.SizeF sizef) return (ValueKind.Point, Make(sizef.Width, sizef.Height));
        if (value is System.Drawing.Rectangle dr) return (ValueKind.Rectangle, Make(dr.X, dr.Y, dr.Width, dr.Height));
        if (value is System.Drawing.RectangleF drf) return (ValueKind.Rectangle, Make(drf.X, drf.Y, drf.Width, drf.Height));
        if (value is TimeSpan span) return (ValueKind.Number, Make(span.TotalSeconds));
        if (value is DateTime date) return (ValueKind.Number, Make((date.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds));
        if (value is not null && !value.GetType().IsEnum && value is IConvertible && value is not string && value is not char)
            try { var n = Convert.ToDouble(value, CultureInfo.InvariantCulture); if (double.IsFinite(n)) return (ValueKind.Number, Make(n)); } catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException) { }
        // Interop data structs are supported by shape, without linking PresentationFramework.
        if (value is not null && value.GetType().IsValueType)
        {
            double? Read(string name)
            {
                var member = value.GetType().GetProperty(name);
                try { return member is null ? null : Convert.ToDouble(member.GetValue(value), CultureInfo.InvariantCulture); } catch { return null; }
            }
            var x = Read("X"); var y = Read("Y"); var w = Read("Width"); var h = Read("Height");
            if (x.HasValue && y.HasValue) return w.HasValue && h.HasValue ? (ValueKind.Rectangle, Make(x.Value, y.Value, w.Value, h.Value)) : (ValueKind.Point, Make(x.Value, y.Value));
            if (w.HasValue && h.HasValue) return (ValueKind.Point, Make(w.Value, h.Value));
            var left = Read("Left"); var top = Read("Top"); var right = Read("Right"); var bottom = Read("Bottom");
            if (left.HasValue && top.HasValue && right.HasValue && bottom.HasValue) return (ValueKind.Thickness, Make(left.Value, top.Value, right.Value, bottom.Value));
        }
        return (value is null || value is string || value?.GetType().IsEnum == true ? ValueKind.Category : ValueKind.Text, Make());
    }
    public static double[] Positions(IReadOnlyList<PlotSample> samples, double compression, DateTime? end = null)
    {
        if (samples.Count == 0) return [];
        var elapsed = Math.Max(1, ((end ?? samples[^1].Timestamp) - samples[0].Timestamp).Ticks);
        return samples.Select((s, i) => (s.Timestamp - samples[0].Timestamp).Ticks / (double)elapsed * (1 - compression) + i / (double)Math.Max(1, samples.Count - 1) * compression).ToArray();
    }
}

internal sealed class HistoryPlot : SceneElement
{
    Texture2D? pixel;
    readonly TextElement[] labels;
    int labelIndex;
    public IReadOnlyList<string> Labels => labels.Where(label => label.isVisible).Select(label => label.Text).ToArray();
    public HistoryPlot(Scene scene) : base(scene)
    {
        Clip = true; Padding = new Thickness(0); ImportedLayout = () => { };
        labels = Enumerable.Range(0, 18).Select(_ => new TextElement(scene)
        {
            Font = Fonts.SegoeUI.get_GetResourceName(10), Clip = true, TextWrapping = TextWrapping.NoWrap,
            ForegroundColor = new(194, 208, 219), isMouseBypassEnabled = true, isVisible = false,
            OrientationReserve = DisplayReservation.FloatBoth, zIndex = 1
        }).ToArray();
        foreach (var label in labels) Children.Add(label);
    }
    static string Number(double value) => value.ToString(Math.Abs(value) >= 1e6 || value != 0 && Math.Abs(value) < .001 ? "0.###E+0" : "0.###", CultureInfo.InvariantCulture);
    void Label(string text, float x, float y, float width = 80, Color? color = null)
    {
        if (labelIndex >= labels.Length) return;
        var label = labels[labelIndex++];
        if (label.Text != text) label.Text = text;
        label.Position = new(x, y); label.Size = new(width, 22); label.ForegroundColor = color ?? new(194, 208, 219); label.isVisible = true;
    }
    public ValueTimeline? Timeline { get; set; }
    public VisualizationMode Mode { get; set; }
    public double Compression { get; set; }
    public double Lower { get; set; }
    public double Upper { get; set; } = 1;
    public bool Invert { get; set; } = true;
    public string Summary { get; private set; } = "Select a value to visualize its live history.";
    public string EventText { get; private set; } = "";
    protected override void Draw(GameTime time)
    {
        labelIndex = 0;
        try { DrawChart(time); }
        finally { for (int index = labelIndex; index < labels.Length; index++) labels[index].isVisible = false; }
    }
    void DrawChart(GameTime time)
    {
        if (pixel is null) { pixel = new Texture2D(spriteBatch.GraphicsDevice, 1, 1); pixel.SetData(new[] { Color.White }); }
        void Fill(float x, float y, float w, float h, Color c) => spriteBatch.Draw(pixel, new Rectangle((int)x, (int)y, Math.Max(1, (int)w), Math.Max(1, (int)h)), c);
        void Line(Vector2 a, Vector2 b, Color c, float thickness = 2)
        {
            var delta = b - a;
            spriteBatch.SpriteBatch.Draw(pixel, a, null, c, MathF.Atan2(delta.Y, delta.X), Vector2.Zero, new Vector2(delta.Length(), thickness), SpriteEffects.None, 0);
        }
        var bounds = new Rectangle((int)Position.X, (int)Position.Y, (int)Size.X, (int)Size.Y);
        Fill(bounds.X, bounds.Y, bounds.Width, bounds.Height, new Color(25, 31, 38));
        var source = Timeline?.GetSnapshots() ?? [];
        int start = Math.Clamp((int)(source.Length * Lower), 0, Math.Max(0, source.Length - 1));
        int count = Math.Max(0, Math.Min(source.Length - start, (int)Math.Ceiling(source.Length * Math.Max(.01, Upper - Lower))));
        var normalized = source.Skip(start).Take(count).Select(Visualization.Normalize).ToArray();
        if (normalized.Length == 0) { Summary = "Select a value to visualize its live history."; EventText = ""; return; }
        var kind = normalized[^1].Kind; var samples = normalized.Select(n => n.Sample).ToArray();
        samples = normalized.Where(n => n.Kind == kind).Select(n => n.Sample).ToArray();
        EventText = string.Join(Environment.NewLine, samples.TakeLast(18).Reverse().Select(s => $"{s.Timestamp:HH:mm:ss.fff}   {s.Text}"));
        Summary = $"{kind} · {samples.Length} samples · {samples[0].Timestamp:HH:mm:ss.fff} – {samples[^1].Timestamp:HH:mm:ss.fff}";
        if (Mode == VisualizationMode.Events || kind == ValueKind.Text) return;
        var box = new Rectangle(bounds.X + 78, bounds.Y + 36, Math.Max(1, bounds.Width - 105), Math.Max(1, bounds.Height - 76));
        bool spatial = (Mode == VisualizationMode.SpatialOverlay || Mode == VisualizationMode.Auto) && kind is ValueKind.Point or ValueKind.Rectangle;
        var end = Upper >= .999 ? DateTime.UtcNow : samples[^1].Timestamp;
        var position = Visualization.Positions(samples, Compression, end);
        for (int i = 0; i < 5; i++)
        {
            Fill(box.X, box.Y + box.Height * i / 4, box.Width, 1, new Color(43, 53, 64));
            Fill(box.X + box.Width * i / 4, box.Y, 1, box.Height, new Color(43, 53, 64));
            if (spatial) continue;
            int index = (int)Math.Round((samples.Length - 1) * i / 4d);
            double fraction = Compression > 0 ? position[index] : i / 4d;
            var elapsed = Compression > 0 ? samples[index].Timestamp - samples[0].Timestamp : TimeSpan.FromTicks((long)((end - samples[0].Timestamp).Ticks * fraction));
            string text = "+" + (elapsed.TotalSeconds >= 1 ? Number(elapsed.TotalSeconds) + " s" : Number(elapsed.TotalMilliseconds) + " ms");
            Label(text, box.X + (float)fraction * box.Width - (i == 4 ? 63 : i == 0 ? 0 : 30), box.Bottom + 9, 85);
        }
        var colors = new[] { new Color(84, 218, 195), new Color(110, 167, 250), new Color(248, 185, 100), new Color(215, 132, 245) };
        if (kind is ValueKind.Boolean or ValueKind.Category or ValueKind.Color)
        {
            Label(kind == ValueKind.Boolean ? "Now: " + samples[^1].Text : samples[^1].Text, box.X, bounds.Y + 7, box.Width);
            if (kind == ValueKind.Boolean)
            {
                Label("True / 1", bounds.X + 7, box.Y + box.Height / 4f - 10, 68, colors[0]);
                Label("False / 0", bounds.X + 7, box.Y + box.Height * .75f - 10, 68);
            }
            var categories = samples.Select(s => s.Text).Distinct().Take(16).ToList();
            for (int i = 0; i < samples.Length; i++)
            {
                float x = box.X + (float)position[i] * box.Width;
                float next = i + 1 < samples.Length ? box.X + (float)position[i + 1] * box.Width : box.Right;
                int category = Math.Max(0, categories.IndexOf(samples[i].Text));
                var color = kind == ValueKind.Color ? samples[i].Color : kind == ValueKind.Boolean ? (samples[i].Channels[0] > 0 ? colors[0] : new Color(83, 92, 108)) : colors[category % colors.Length];
                float y = kind == ValueKind.Boolean ? box.Y + (samples[i].Channels[0] > 0 ? 0 : box.Height / 2f) : box.Y;
                Fill(x, y, Math.Max(2, next - x), kind == ValueKind.Boolean ? box.Height / 2 : box.Height, color * .7f);
            }
            return;
        }
        var channels = samples.SelectMany(s => s.Channels).Where(double.IsFinite).ToArray();
        if (channels.Length == 0) return;
        double min = channels.Min(), max = channels.Max();
        double padding = max == min ? Math.Max(1, Math.Abs(min) * .1) : (max - min) * .08; min -= padding; max += padding;
        float Y(double v) => box.Bottom - (float)Math.Clamp((v - min) / (max - min), 0, 1) * box.Height;
        if (spatial && kind is ValueKind.Point or ValueKind.Rectangle)
        {
            double minX = samples.Min(s => s.Channels[0]), maxX = samples.Max(s => s.Channels[0] + (kind == ValueKind.Rectangle ? s.Channels[2] : 0));
            double minY = samples.Min(s => s.Channels[1]), maxY = samples.Max(s => s.Channels[1] + (kind == ValueKind.Rectangle ? s.Channels[3] : 0));
            var extentX = Math.Max(1, maxX - minX); var extentY = Math.Max(1, maxY - minY);
            var latest = samples[^1].Channels;
            Label("X " + Number(latest[0]) + "   Y " + Number(latest[1]) + (kind == ValueKind.Rectangle ? "   W " + Number(latest[2]) + "   H " + Number(latest[3]) : ""), box.X, bounds.Y + 7, box.Width, colors[0]);
            for (int i = 0; i < 5; i++)
            {
                double fraction = i / 4d;
                Label(Number(Invert ? minY + extentY * fraction : minY + extentY * (1 - fraction)), bounds.X + 7, box.Y + (float)fraction * box.Height - 10, 68);
                Label(Number(minX + extentX * fraction), box.X + (float)fraction * box.Width - (i == 4 ? 60 : i == 0 ? 0 : 25), box.Bottom + 9, 75);
            }
            Vector2 Map(double x, double y) => new(box.X + (float)((x - minX) / extentX) * box.Width, box.Y + (float)(Invert ? (y - minY) / extentY : 1 - (y - minY) / extentY) * box.Height);
            Vector2? prior = null;
            for (int i = 0; i < samples.Length; i++)
            {
                var c = samples[i].Channels; var point = Map(c[0], c[1]); var color = Color.Lerp(colors[1] * .2f, colors[0], (float)(i + 1) / samples.Length);
                if (kind == ValueKind.Rectangle)
                {
                    var corner = Map(c[0] + c[2], c[1] + c[3]);
                    Line(point, new(corner.X, point.Y), color); Line(new(corner.X, point.Y), corner, color);
                    Line(corner, new(point.X, corner.Y), color); Line(new(point.X, corner.Y), point, color);
                }
                else { if (prior is { } p) Line(p, point, color); Fill(point.X - 2, point.Y - 2, 4, 4, color); }
                prior = point;
            }
            return;
        }
        for (int i = 0; i < 5; i++) Label(Number(max - (max - min) * i / 4d), bounds.X + 7, box.Y + box.Height * i / 4f - 10, 68);
        string[] channelNames = kind == ValueKind.Point ? ["X", "Y", "Z"] : kind == ValueKind.Rectangle ? ["X", "Y", "W", "H"] : kind == ValueKind.Thickness ? ["L", "T", "R", "B"] : ["Value"];
        int channelCount = Math.Min(4, samples[^1].Channels.Length);
        for (int channel = 0; channel < channelCount; channel++)
            Label(channelNames[channel] + " " + Number(samples[^1].Channels[channel]), box.X + channel * box.Width / channelCount, bounds.Y + 7, box.Width / channelCount, colors[channel]);
        for (int channel = 0; channel < Math.Min(4, samples[0].Channels.Length); channel++)
        {
            for (int i = 1; i < samples.Length; i++)
            {
                var a = new Vector2(box.X + (float)position[i - 1] * box.Width, Y(samples[i - 1].Channels[channel]));
                var b = new Vector2(box.X + (float)position[i] * box.Width, Y(samples[i].Channels[channel]));
                // Hold the old value across an elapsed-time gap, then draw the change.
                Line(a, new(b.X, a.Y), colors[channel]); Line(new(b.X, a.Y), b, colors[channel]);
            }
            if (samples.Length == 1) Line(new(box.X, Y(samples[0].Channels[channel])), new(box.Right, Y(samples[0].Channels[channel])), colors[channel]);
            else Line(new(box.X + (float)position[^1] * box.Width, Y(samples[^1].Channels[channel])), new(box.Right, Y(samples[^1].Channels[channel])), colors[channel]);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) pixel?.Dispose(); base.Dispose(disposing); }
}
