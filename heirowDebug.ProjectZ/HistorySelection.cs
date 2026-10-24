namespace heirowDebug.ProjectZ;

internal sealed record HistorySlice(ValueSnapshot[] Samples, DateTime EndTime, bool IsSnapshot);

// Percentages refer to one fixed history while inspecting the past. Capturing
// the source array also keeps that view intact when live retention rolls over.
internal sealed class HistorySelection
{
    ValueTimeline? timeline;
    ValueSnapshot[]? snapshot;
    double lower, upper = 1;
    public bool IsSnapshot => upper < 1;
    public ValueTimeline? Timeline
    {
        get => timeline;
        set
        {
            if (ReferenceEquals(timeline, value)) return;
            timeline = value;
            snapshot = IsSnapshot ? timeline?.GetSnapshots() ?? [] : null;
        }
    }
    public double Lower { get => lower; set { if (double.IsFinite(value)) lower = Math.Clamp(value, 0, 1); } }
    public double Upper
    {
        get => upper;
        set
        {
            if (!double.IsFinite(value)) return;
            upper = Math.Clamp(value, 0, 1);
            if (!IsSnapshot) snapshot = null;
            else snapshot ??= timeline?.GetSnapshots() ?? [];
        }
    }
    public HistorySlice Read(DateTime now)
    {
        var source = snapshot ?? timeline?.GetSnapshots() ?? [];
        if (source.Length == 0) return new([], IsSnapshot ? DateTime.MinValue : now, IsSnapshot);
        int start = Math.Clamp((int)(source.Length * lower), 0, source.Length - 1);
        int end = Math.Clamp((int)Math.Ceiling(source.Length * upper), start + 1, source.Length);
        var visible = source[start..end];
        return new(visible, IsSnapshot || now < visible[^1].Timestamp ? visible[^1].Timestamp : now, IsSnapshot);
    }
}
