using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using ProjectZ.Shared.Drawing;

namespace heirowDebug.ProjectZ;

public static class Utils
{
    public enum DebugProfile { NearInstant, Fast, Medium, Slow, PowerSaving }
    static DebugProfile profile = DebugProfile.Fast;
    static int fps = 60;
    public static int FramesPerSecond { get => fps; set => fps = Math.Clamp(value, 1, 1000); }
    public static TimeSpan Interval => TimeSpan.FromSeconds(1d / FramesPerSecond);
    public static double IntervalMilliseconds => Interval.TotalMilliseconds;
    public static DebugProfile Profile
    {
        get => profile;
        set
        {
            profile = value;
            (FramesPerSecond, ValueTimeline.MaximumSnapshotsPerValue) = value switch
            {
                DebugProfile.NearInstant => (120, 4000), DebugProfile.Fast => (60, 2000),
                DebugProfile.Medium => (30, 1000), DebugProfile.Slow => (10, 500), _ => (1, 250)
            };
        }
    }
}

public sealed record ValueSnapshot(DateTime Timestamp, object? Value);

public sealed class ValueTimeline
{
    readonly object gate = new();
    readonly Queue<ValueSnapshot> samples = new();
    ValueSnapshot? latest;
    public static int MaximumSnapshotsPerValue { get; set; } = 2000;
    public int Count { get { lock (gate) return samples.Count; } }
    public ValueSnapshot[] GetSnapshots() { lock (gate) return samples.ToArray(); }
    internal bool Add(object? value, DateTime timestamp)
    {
        lock (gate)
        {
            // Immutable copies keep collection history independent of later mutations.
            value = SnapshotValue(value);
            if (latest is not null && Equivalent(latest.Value, value)) return false;
            if (latest is not null && timestamp < latest.Timestamp) timestamp = latest.Timestamp;
            latest = new(timestamp, value); samples.Enqueue(latest);
            while (samples.Count > Math.Max(1, MaximumSnapshotsPerValue)) samples.Dequeue();
            return true;
        }
    }
    public void Clear() { lock (gate) { samples.Clear(); latest = null; } }
    internal static object? SnapshotValue(object? value)
    {
        if (value is string || value is null || value.GetType().IsValueType) return value;
        if (value is IDictionary dictionary)
        {
            var entries = new List<string>(); var iterator = dictionary.GetEnumerator();
            while (entries.Count < 192 && iterator.MoveNext()) entries.Add($"{iterator.Key}: {iterator.Value}");
            return entries.ToArray();
        }
        if (value is IList list) return list.Cast<object?>().Take(192).Select(Format).ToArray();
        return Format(value);
    }
    static bool Equivalent(object? a, object? b) => a is string[] aa && b is string[] bb ? aa.SequenceEqual(bb) : Equals(a, b);
    public static string Format(object? value)
    {
        try { return value is null ? "null" : value is string[] array ? string.Join(", ", array) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null"; }
        catch (Exception ex) { return $"<{ex.GetType().Name}>"; }
    }
}

public sealed class DebugValue
{
    readonly DebugWatcher owner;
    readonly Func<object?>? getter;
    readonly Action<object?>? setter;
    readonly string? customName;
    readonly Type? customType;
    readonly object recordGate = new();
    IDisposable? propertyHook;
    internal DebugValue(DebugWatcher owner, MemberInfo member) { this.owner = owner; Member = member; }
    internal DebugValue(DebugWatcher owner, string name, Type type, Func<object?> getter, Action<object?>? setter = null)
    { this.owner = owner; customName = name; customType = type; this.getter = getter; this.setter = setter; }
    public string Guid { get; } = System.Guid.NewGuid().ToString();
    public string Name => customName ?? Member!.Name;
    public Type Type => customType ?? (Member is FieldInfo info ? info.FieldType : ((PropertyInfo)Member!).PropertyType);
    public MemberInfo? Member { get; }
    public object? Value { get; private set; }
    public string? Error { get; private set; }
    public ValueTimeline Timeline { get; } = new();
    public ValueObservationMode ObservationMode => propertyHook is not null ? ValueObservationMode.PropertyHook :
        Member is PropertyInfo && owner.HasChangeNotifications ? ValueObservationMode.ChangeNotification : ValueObservationMode.Polling;
    public string? ObservationError { get; private set; }
    public bool CanHookProperty => Member is PropertyInfo && !owner.TargetType.IsValueType;
    internal bool IsObservationEnabled => owner.IsObservationEnabled;
    internal bool MatchesInstance(object? instance) => owner.IsStaticRoot ? instance is null : ReferenceEquals(instance, owner.AttachedObject);
    public bool TryEnablePropertyHooks(out string? error)
    {
        if (propertyHook is not null) { error = null; return true; }
        try { propertyHook = PropertyObservation.Attach(this); ObservationError = error = null; return true; }
        catch (Exception ex) { ObservationError = error = (ex.InnerException ?? ex).Message; return false; }
    }
    public void DisablePropertyHooks() => Interlocked.Exchange(ref propertyHook, null)?.Dispose();
    public bool CanWrite => owner.CanWriteChildren && (getter is not null ? setter is not null : Member is FieldInfo f ? !f.IsInitOnly && !f.IsLiteral : ((PropertyInfo)Member!).SetMethod is not null);
    internal object? Read() => getter is not null ? getter() : Member is FieldInfo f ? f.GetValue(owner.AttachedObject) : ((PropertyInfo)Member!).GetValue(owner.AttachedObject);
    internal void Sample(DateTime now)
    {
        try { Record(Read(), now); }
        catch (Exception ex) { Error = (ex.InnerException ?? ex).Message; }
    }
    internal void Record(object? value, DateTime now)
    {
        bool changed;
        lock (recordGate) { Value = value; Error = ObservationError = null; changed = Timeline.Add(value, now); }
        owner.Recorded(this, changed);
    }
    internal void RecordObservationError(Exception ex) => ObservationError = (ex.InnerException ?? ex).Message;
    public Task ApplyAsync(string text, bool alsoSetBackingField = false) => owner.DispatchAsync(() =>
    {
        if (!CanWrite) throw new InvalidOperationException("This member is read-only.");
        var value = ConvertText(text, Type);
        WriteRaw(value);
        if (alsoSetBackingField && Member is PropertyInfo)
        {
            var backing = owner.TargetType.GetField("_" + Name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (backing is not null && !backing.IsInitOnly && backing.FieldType == Type) backing.SetValue(owner.AttachedObject, value);
        }
        owner.PropagateValueType();
        Sample(DateTime.UtcNow);
    });
    internal void WriteRaw(object? value)
    {
        if (setter is not null) setter(value);
        else if (Member is FieldInfo field) field.SetValue(owner.AttachedObject, value);
        else ((PropertyInfo)Member!).SetValue(owner.AttachedObject, value);
    }
    public static object? ConvertText(string text, Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null && string.Equals(text, "null", StringComparison.OrdinalIgnoreCase)) return null;
        type = nullable ?? type;
        if (type == typeof(string)) return text;
        if (type.IsEnum) return Enum.Parse(type, text, true);
        if (type == typeof(Guid)) return System.Guid.Parse(text);
        if (type == typeof(TimeSpan)) return TimeSpan.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(DateTime)) return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(Point) || type == typeof(Rectangle) || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4))
        {
            var parts = text.Trim('(', ')', '{', '}').Split([',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            if (type == typeof(Point) && parts.Length == 2) return new Point(checked((int)parts[0]), checked((int)parts[1]));
            if (type == typeof(Rectangle) && parts.Length == 4) return new Rectangle(checked((int)parts[0]), checked((int)parts[1]), checked((int)parts[2]), checked((int)parts[3]));
            if (type == typeof(Vector2) && parts.Length == 2) return new Vector2(parts[0], parts[1]);
            if (type == typeof(Vector3) && parts.Length == 3) return new Vector3(parts[0], parts[1], parts[2]);
            if (type == typeof(Vector4) && parts.Length == 4) return new Vector4(parts[0], parts[1], parts[2], parts[3]);
            throw new FormatException("Enter the geometry channels separated by commas.");
        }
        if (type == typeof(Color))
        {
            var hex = text.TrimStart('#');
            if (hex.Length == 6) return new Color(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
            if (hex.Length == 8) return new Color(Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16), Convert.ToByte(hex[6..8], 16), Convert.ToByte(hex[..2], 16));
        }
        var converter = TypeDescriptor.GetConverter(type);
        if (converter.CanConvertFrom(typeof(string))) return converter.ConvertFromInvariantString(text);
        throw new NotSupportedException($"Editing {type.Name} from text is not supported.");
    }
}

/// <summary>Bounded reflection watcher. Attach a Scene to sample and edit on its owning thread.</summary>
public sealed class DebugWatcher : IDisposable
{
    static readonly object RegistryGate = new();
    static readonly Dictionary<string, DebugWatcher> registry = new();
    static long revision;
    public static long Revision => Interlocked.Read(ref revision);
    readonly object gate = new();
    readonly List<DebugValue> values;
    readonly List<DebugWatcher> children = new();
    object[]? collectionKeys;
    DebugValue? parentValueSource;
    readonly ConcurrentQueue<(Action Action, TaskCompletionSource Completion)> commands = new();
    readonly SynchronizationContext? context;
    readonly ConcurrentDictionary<DebugValue, bool> pendingChanges = new();
    INotifyPropertyChanged? notifications;
    [ThreadStatic] static HashSet<DebugWatcher>? notifying;
    Scene? scene;
    Timer? timer;
    int sceneThread;
    int memberIndex;
    long nextSample;
    volatile bool disposed;
    volatile bool active;
    int sampling;
    public static IReadOnlyDictionary<string, DebugWatcher> Watchers { get { lock (RegistryGate) return new Dictionary<string, DebugWatcher>(registry); } }
    public static int MaximumWatcherCount { get; set; } = 24;
    public static int MaximumChildWatchersPerParent { get; set; } = 6;
    public static int MaximumMembersPerWatcher { get; set; } = 192;
    public static int MaximumMembersPerPass { get; set; } = 8;
    public static int MaxRecursionDepth { get; set; } = 2;
    public static DebugWindow? DebugWindow { get; private set; }
    public string Guid { get; } = System.Guid.NewGuid().ToString();
    public string Name { get; }
    public object? AttachedObject { get; }
    public Type TargetType { get; }
    public bool IsStaticRoot { get; }
    public bool IsRecursive { get; set; }
    public DebugWatcher? Parent { get; }
    public bool isChild => Parent is not null;
    public string? ParentValueGuid { get; private set; }
    public IReadOnlyList<DebugWatcher> Children { get { lock (RegistryGate) return children.ToArray(); } }
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;
    public bool isEnabled { get => active; set => active = value; }
    public IReadOnlyList<DebugValue> Values => values;
    public int ValueCount => values.Count;
    internal Scene? OwnerScene => scene;
    internal bool HasChangeNotifications => notifications is not null;
    internal bool IsObservationEnabled => !disposed && isEnabled;
    internal bool CanWriteChildren => !TargetType.IsValueType || parentValueSource?.CanWrite == true;
    internal void PropagateValueType()
    {
        if (!TargetType.IsValueType || parentValueSource is null) return;
        parentValueSource.WriteRaw(AttachedObject); Parent?.PropagateValueType();
    }
    public event Action<DebugWatcher, DebugValue>? ValueChanged;
    public event Action<DebugWatcher, DebugValue>? ValueCalculated;
    public DebugWatcher(object target, bool Recursive = false, string? name = null) : this(target, target.GetType(), Recursive, name, null, false) { }
    DebugWatcher(object? target, Type type, bool recursive, string? name, DebugWatcher? parent, bool isStatic)
    {
        AttachedObject = target; TargetType = type; IsRecursive = recursive; Parent = parent; IsStaticRoot = isStatic;
        Name = name ?? type.FullName ?? type.Name; context = SynchronizationContext.Current;
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        values = type.GetFields(flags).Where(f => !f.Name.Contains("k__BackingField", StringComparison.Ordinal)).Cast<MemberInfo>()
            .Concat(type.GetProperties(flags).Where(p => p.GetMethod is not null && p.GetIndexParameters().Length == 0))
            .OrderBy(m => m.Name, StringComparer.Ordinal).Take(Math.Max(0, MaximumMembersPerWatcher)).Select(m => new DebugValue(this, m)).ToList();
        if (target is IDictionary dictionary)
        {
            values.Clear();
            collectionKeys = dictionary.Keys.Cast<object>().Take(MaximumMembersPerWatcher).ToArray();
            foreach (var key in collectionKeys)
            {
                var item = dictionary[key]; var itemType = item?.GetType() ?? typeof(string);
                values.Add(new(this, "[" + ValueTimeline.Format(key) + "]", itemType, () => dictionary[key], dictionary.IsReadOnly ? null : v => dictionary[key] = v));
            }
        }
        else if (target is IList list)
        {
            values.Clear();
            for (int i = 0; i < Math.Min(list.Count, MaximumMembersPerWatcher); i++)
            {
                int index = i; var itemType = list[i]?.GetType() ?? typeof(string);
                values.Add(new(this, "[" + i + "]", itemType, () => index < list.Count ? list[index] : null, list.IsReadOnly ? null : v => list[index] = v));
            }
        }
        lock (RegistryGate)
        {
            if (registry.Count >= MaximumWatcherCount) throw new InvalidOperationException("The watcher safety limit has been reached.");
            registry.Add(Guid, this);
            Interlocked.Increment(ref revision);
        }
        if (target is Scene targetScene) AttachScene(targetScene);
        else if (target is SceneManager manager && manager.ActiveScene is { } active) AttachScene(active);
        else if (parent?.scene is { } parentScene) AttachScene(parentScene);
    }
    public static DebugWatcher WatchWindow(object window, bool Recursive = false) => WatchStaticObject(window, "[Window] " + window.GetType().Name, Recursive);
    public static DebugWatcher WatchStaticObject(object instance, string sourceName, bool Recursive = false)
    {
        lock (RegistryGate)
        {
            var existing = registry.Values.FirstOrDefault(w => !w.isChild && ReferenceEquals(w.AttachedObject, instance));
            return existing ?? new DebugWatcher(instance, Recursive, sourceName);
        }
    }
    public static DebugWatcher WatchStaticType(Type type, bool Recursive = false)
    {
        if (type.ContainsGenericParameters) throw new ArgumentException("Open generic types cannot be watched.", nameof(type));
        lock (RegistryGate) return registry.Values.FirstOrDefault(w => w.IsStaticRoot && w.TargetType == type) ?? new(null, type, Recursive, "[Static] " + type.FullName, null, true);
    }
    public void AttachScene(Scene owner)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ReferenceEquals(scene, owner)) return;
        if (scene is not null) scene.FrameUpdated -= OnFrame;
        timer?.Dispose(); timer = null; scene = owner; sceneThread = Environment.CurrentManagedThreadId;
        owner.FrameUpdated += OnFrame;
    }
    public void StartSession()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        isEnabled = true;
        if (notifications is null && AttachedObject is INotifyPropertyChanged source)
        {
            notifications = source; source.PropertyChanged += OnPropertyChanged;
        }
        if (scene is null) timer ??= new Timer(_ =>
        {
            if (context is null) Sample(); else context.Post(_ => Sample(), null);
            try { if (!disposed) timer?.Change(Utils.Interval, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
        }, null, Utils.Interval, Timeout.InfiniteTimeSpan);
    }
    public void StopSession() { isEnabled = false; foreach (var child in children.ToArray()) child.StopSession(); }
    void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!IsObservationEnabled) return;
        notifying ??= new(); if (!notifying.Add(this)) return;
        try
        {
            foreach (var value in values.Where(v => v.Member is PropertyInfo && (string.IsNullOrEmpty(args.PropertyName) || v.Name == args.PropertyName)).ToArray())
                try { value.Record(value.Read(), DateTime.UtcNow); } catch (Exception ex) { value.RecordObservationError(ex); }
        }
        catch (Exception) { /* Observation must never interrupt the application's event. */ }
        finally { notifying.Remove(this); }
    }
    internal void Recorded(DebugValue value, bool changed)
    {
        pendingChanges.AddOrUpdate(value, changed, (_, previous) => previous || changed);
        if (changed) Interlocked.Increment(ref revision);
    }
    void DrainChanges()
    {
        foreach (var value in pendingChanges.Keys)
            if (pendingChanges.TryRemove(value, out bool changed))
            {
                ValueCalculated?.Invoke(this, value);
                if (changed) ValueChanged?.Invoke(this, value);
            }
    }
    void OnFrame(GameTime _) { sceneThread = Environment.CurrentManagedThreadId; DrainCommands(); Sample(); DrainChanges(); }
    public void SampleNow() { if (scene is not null && Environment.CurrentManagedThreadId != sceneThread) throw new InvalidOperationException("Sample on the owning scene thread."); Sample(true); }
    void Sample(bool force = false)
    {
        if (disposed || !isEnabled || Interlocked.Exchange(ref sampling, 1) != 0) return;
        try
        {
            long now = Environment.TickCount64;
            if (!force && now < nextSample) return;
            nextSample = now + Math.Max(1, (long)Utils.IntervalMilliseconds);
            lock (gate)
            {
                RefreshCollectionMembers();
                for (int i = 0; i < Math.Min(values.Count, Math.Max(1, MaximumMembersPerPass)); i++)
                {
                    memberIndex %= values.Count; var value = values[memberIndex]; memberIndex = (memberIndex + 1) % values.Count;
                    // Prime once, then let notifications/accessor callbacks record
                    // every transition without repeatedly invoking the getter.
                    if (force || value.ObservationMode == ValueObservationMode.Polling || value.Timeline.Count == 0)
                        value.Sample(DateTime.UtcNow);
                    if (IsRecursive && Depth < MaxRecursionDepth && children.Count < MaximumChildWatchersPerParent && IsInspectable(value.Value))
                        TryExpand(value);
                }
            }
        }
        finally { Volatile.Write(ref sampling, 0); DrainChanges(); }
    }
    void RefreshCollectionMembers()
    {
        if (AttachedObject is IDictionary dictionary)
        {
            var keys = dictionary.Keys.Cast<object>().Take(MaximumMembersPerWatcher).ToArray();
            if (collectionKeys is not null && collectionKeys.SequenceEqual(keys)) return;
            var old = values.ToDictionary(v => v.Name, StringComparer.Ordinal); values.Clear(); collectionKeys = keys;
            foreach (var key in keys)
            {
                string name = "[" + ValueTimeline.Format(key) + "]";
                values.Add(old.TryGetValue(name, out var existing) ? existing : new(this, name, dictionary[key]?.GetType() ?? typeof(string), () => dictionary[key], dictionary.IsReadOnly ? null : v => dictionary[key] = v));
            }
        }
        else if (AttachedObject is IList list)
        {
            int count = Math.Clamp(list.Count, 0, MaximumMembersPerWatcher);
            while (values.Count > count) { values[^1].Timeline.Clear(); values.RemoveAt(values.Count - 1); }
            while (values.Count < count)
            {
                int index = values.Count;
                values.Add(new(this, "[" + index + "]", list[index]?.GetType() ?? typeof(string), () => index < list.Count ? list[index] : null, list.IsReadOnly ? null : v => list[index] = v));
            }
        }
    }
    static bool IsInspectable(object? value) => value is not null && value is not string && !value.GetType().IsPrimitive && !value.GetType().IsEnum && value is not decimal && value is not DateTime && value is not TimeSpan && value is not Delegate && value is not Type;
    public bool CanExpand(DebugValue value)
    {
        if (!values.Contains(value) || !IsInspectable(value.Value) || Depth >= MaxRecursionDepth) return false;
        for (var ancestor = this; ancestor is not null; ancestor = ancestor.Parent) if (ReferenceEquals(ancestor.AttachedObject, value.Value)) return false;
        return true;
    }
    public DebugWatcher? Expand(DebugValue value) => TryExpand(value);
    DebugWatcher? TryExpand(DebugValue value)
    {
        if (!CanExpand(value)) return null;
        lock (RegistryGate)
        {
            var existing = children.FirstOrDefault(c => c.ParentValueGuid == value.Guid);
            if (existing is not null && ReferenceEquals(existing.AttachedObject, value.Value)) return existing;
            existing?.Dispose();
            if (registry.Count >= MaximumWatcherCount || children.Count >= MaximumChildWatchersPerParent) return null;
            var child = new DebugWatcher(value.Value, value.Value!.GetType(), IsRecursive, value.Name, this, false);
            child.ParentValueGuid = value.Guid;
            child.parentValueSource = value;
            children.Add(child); if (isEnabled) child.StartSession(); return child;
        }
    }
    internal Task DispatchAsync(Action action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (scene is null && context is null || scene is not null && Environment.CurrentManagedThreadId == sceneThread) { action(); return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (scene is not null) commands.Enqueue((action, completion));
        else context!.Post(_ => { try { action(); completion.SetResult(); } catch (Exception ex) { completion.SetException(ex); } }, null);
        return AwaitCommand(completion);
    }
    static async Task AwaitCommand(TaskCompletionSource completion)
    {
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch (TimeoutException ex) { completion.TrySetException(ex); throw; }
    }
    void DrainCommands()
    {
        while (commands.TryDequeue(out var command))
        {
            if (command.Completion.Task.IsCompleted) continue;
            try { command.Action(); command.Completion.TrySetResult(); } catch (Exception ex) { command.Completion.TrySetException(ex); }
        }
    }
    public static void CreateDebugWindow()
    {
        DebugWindow ??= new DebugWindow(); DebugWindow.Show();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; isEnabled = false; timer?.Dispose();
        if (notifications is not null) notifications.PropertyChanged -= OnPropertyChanged;
        notifications = null;
        if (scene is not null) scene.FrameUpdated -= OnFrame;
        foreach (var child in children.ToArray()) child.Dispose();
        while (commands.TryDequeue(out var command)) command.Completion.TrySetException(new ObjectDisposedException(Name));
        foreach (var value in values) { value.DisablePropertyHooks(); value.Timeline.Clear(); }
        pendingChanges.Clear();
        lock (RegistryGate) { registry.Remove(Guid); Parent?.children.Remove(this); Interlocked.Increment(ref revision); }
    }
}
