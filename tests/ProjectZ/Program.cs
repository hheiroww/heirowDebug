using heirowDebug.ProjectZ;
using Microsoft.Xna.Framework;
using ProjectZ.Shared.Drawing;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using NativeWindow = heirowDebug.ProjectZ.NativeWindow;

static class Program
{
    static int Main(string[] args)
    {
        try
        {
            if (args is ["--runtime", var result]) { Runtime(result); return 0; }
            if (args is ["--protected-runtime", var executable, var resultFile]) { ProtectedRuntime(executable, resultFile); return 0; }
            Core(); Observation(); SnapshotSelection(); ExpandedValues(); Console.WriteLine("PASS bounded sampling, snapshots, editing, cycles, static targets, profiles, native visualization types, notifications, getter/setter hooks, frozen history selection and stable expanded structs"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    sealed class Sample
    {
        public double Number = 7;
        public int IdleValue;
        public bool Boolean = true;
        public List<int> Items = [1, 2, 3];
        public Dictionary<string, int> Dictionary = new() { ["first"] = 1 };
        public Vector2 Point = new(10, 20);
        public Rectangle Rectangle = new(1, 2, 30, 40);
        public Color Tint = Color.Coral;
        public Sample? Cycle;
        public int Reads;
        public int SideEffect { get { Reads++; return Reads; } }
        public int Failure => throw new InvalidOperationException("getter failure");
        string text = "before";
        public string Text
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get => text;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set => text = value;
        }
        public string LongLabel = "Long watcher text must stay in the tree column: " + new string('W', 180);
    }
    static class Statics { public static int Value = 12; public static int GetterReads; public static object Deferred { get { GetterReads++; return new object(); } } }
    static void Core()
    {
        Utils.Profile = Utils.DebugProfile.PowerSaving;
        DebugWatcher.MaximumMembersPerPass = 192;
        var sample = new Sample(); sample.Cycle = sample;
        using var watcher = new DebugWatcher(sample);
        Check(sample.Reads == 0, "Construction invoked a property getter.");
        watcher.isEnabled = true; watcher.SampleNow();
        var number = watcher.Values.Single(v => v.Name == nameof(Sample.Number));
        Check((double)number.Value! == 7, "Numeric field was not sampled.");
        Check(number.LastChangedTimestamp == 0, "Initial observation started an update glow.");
        sample.Number = 9; watcher.SampleNow();
        Check(number.Timeline.GetSnapshots().Length == 2, "Changes were not recorded.");
        long changedAt = number.LastChangedTimestamp;
        Check(changedAt > 0, "A value change did not start the update animation.");
        watcher.SampleNow();
        Check(number.LastChangedTimestamp == changedAt, "An unchanged polling pass restarted the update animation.");
        number.ApplyAsync("15.5").GetAwaiter().GetResult(); Check(sample.Number == 15.5, "Live field write failed.");
        var text = watcher.Values.Single(v => v.Name == nameof(Sample.Text)); text.ApplyAsync("after").GetAwaiter().GetResult(); Check(sample.Text == "after", "Property write failed.");
        var items = watcher.Values.Single(v => v.Name == nameof(Sample.Items)); sample.Items[0] = 42; watcher.SampleNow();
        Check(((string[])items.Timeline.GetSnapshots()[0].Value!)[0] == "1", "Collection snapshot changed with its source.");
        using var collection = watcher.Expand(items)!;
        Check(collection is not null && collection.Values.Count == 3, "Collection entries did not expand.");
        collection!.isEnabled = true; collection.SampleNow();
        collection.Values[1].ApplyAsync("99").GetAwaiter().GetResult(); Check(sample.Items[1] == 99, "Collection element editing failed.");
        sample.Items.Add(4); collection.SampleNow(); Check(collection.Values.Count == 4, "New collection entries were not discovered.");
        sample.Items.RemoveAt(3); collection.SampleNow(); Check(collection.Values.Count == 3, "Removed collection entries were retained.");
        var dictionary = watcher.Values.Single(v => v.Name == nameof(Sample.Dictionary));
        Check(dictionary.Error is null && dictionary.Timeline.GetSnapshots().Length > 0, "Dictionary snapshot failed.");
        Check((Vector2)DebugValue.ConvertText("3,4", typeof(Vector2))! == new Vector2(3, 4), "Native geometry conversion failed.");
        using var geometry = watcher.Expand(watcher.Values.Single(v => v.Name == nameof(Sample.Point)))!;
        Check(geometry is not null && geometry.Values.Any(v => v.Name == "X"), "Native structs did not expand.");
        geometry!.Values.Single(v => v.Name == "X").ApplyAsync("17").GetAwaiter().GetResult(); Check(sample.Point.X == 17, "Nested struct edit did not propagate to the live parent.");
        Check(watcher.Values.Single(v => v.Name == nameof(Sample.Failure)).Error == "getter failure", "Getter failures were not isolated.");
        Check(watcher.Expand(watcher.Values.Single(v => v.Name == nameof(Sample.Cycle))) is null, "Ancestor cycle was expanded.");
        var reads = Statics.GetterReads;
        using var statics = DebugWatcher.WatchStaticType(typeof(Statics)); Check(Statics.GetterReads == reads, "Static browsing ran getters.");
        statics.isEnabled = true; statics.SampleNow();
        Check((int)statics.Values.Single(v => v.Name == nameof(Statics.Value)).Value! == 12, "Static value was not sampled.");
        ValueTimeline.MaximumSnapshotsPerValue = 3;
        for (int i = 0; i < 20; i++) { sample.Number++; watcher.SampleNow(); }
        Check(number.Timeline.GetSnapshots().Length == 3, "Snapshot retention is unbounded.");
        var types = new (object Value, ValueKind Kind)[] { (true, ValueKind.Boolean), (1d, ValueKind.Number), ("abc", ValueKind.Category), (Color.Coral, ValueKind.Color), (new Vector2(2, 3), ValueKind.Point), (new Rectangle(1, 2, 3, 4), ValueKind.Rectangle), (new Vector4(1, 2, 3, 4), ValueKind.Thickness) };
        foreach (var (value, kind) in types) Check(Visualization.Normalize(new(DateTime.UtcNow, value)).Kind == kind, "Wrong plot kind: " + kind);
        Utils.Profile = Utils.DebugProfile.NearInstant; Check(Utils.FramesPerSecond == 120 && ValueTimeline.MaximumSnapshotsPerValue == 4000, "Profile did not update retention and frequency.");
        var time = DateTime.UtcNow; var positions = Visualization.Positions([new(time, [], "", Color.White), new(time.AddSeconds(1), [], "", Color.White), new(time.AddSeconds(10), [], "", Color.White)], 0);
        Check(Math.Abs(positions[1] - .1) < .001, "Elapsed-time spacing lost a gap.");
        var compact = Visualization.Positions([new(time, [], "", Color.White), new(time.AddSeconds(1), [], "", Color.White), new(time.AddSeconds(10), [], "", Color.White)], 1);
        Check(Math.Abs(compact[1] - .5) < .001, "Gap compression was not applied.");
        DebugWatcher.MaximumMembersPerPass = 8; Utils.Profile = Utils.DebugProfile.Fast;
    }
    sealed class HookSample
    {
        double number;
        public double Number
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get => number;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set => number = Math.Clamp(value, 0, 100);
        }
        public int Raw;
        public int Computed { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get => Raw * 2; }
    }
    static class HookStatics
    {
        static int value;
        public static int Value
        {
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get => value;
            [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set => HookStatics.value = value;
        }
    }
    sealed class NotifyingSample : System.ComponentModel.INotifyPropertyChanged
    {
        int number;
        public int GetterCalls;
        public int Number { get { Interlocked.Increment(ref GetterCalls); return number; } set { number = value; PropertyChanged?.Invoke(this, new(nameof(Number))); } }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
    static void Observation()
    {
        Utils.Profile = Utils.DebugProfile.PowerSaving;
        var model = new HookSample(); var unrelated = new HookSample();
        using (var watcher = new DebugWatcher(model))
        {
            watcher.isEnabled = true; watcher.SampleNow();
            var value = watcher.Values.Single(v => v.Name == nameof(HookSample.Number));
            Check(value.TryEnablePropertyHooks(out var error), "Could not hook instance accessors: " + error);
            for (int i = 1; i <= 8; i++) model.Number = i;
            Check(value.Timeline.Count == 9 && (double)value.Value! == 8, "Setter hooks missed transitions between scene frames.");
            model.Number = 500; Check((double)value.Value! == 100, "Setter hook recorded its input instead of the actual clamped result.");
            unrelated.Number = 17; Check((double)value.Value! == 100, "A hook recorded another object's property.");
            using (var other = new DebugWatcher(unrelated))
            {
                other.isEnabled = true; var otherValue = other.Values.Single(v => v.Name == nameof(HookSample.Number));
                Check(otherValue.TryEnablePropertyHooks(out error), "A shared accessor hook failed: " + error);
                unrelated.Number = 27;
                Check((double)otherValue.Value! == 27 && (double)value.Value! == 100, "Shared accessor hooks mixed object identities.");
            }
            model.Number = 99; Check((double)value.Value! == 99, "Disposing another watcher removed the remaining hook.");
            var computed = watcher.Values.Single(v => v.Name == nameof(HookSample.Computed));
            Check(computed.TryEnablePropertyHooks(out error), "Read-only getter could not be hooked: " + error);
            model.Raw = 21; _ = model.Computed;
            Check((int)computed.Value! == 42, "Getter hook did not capture the application's return value.");
            watcher.isEnabled = false; model.Number = 20; Check((double)value.Value! == 99, "Paused hooks continued capturing.");
            watcher.isEnabled = true; value.DisablePropertyHooks(); model.Number = 30;
            Check(value.ObservationMode == ValueObservationMode.Polling && (double)value.Value! == 99, "Disabling a hook left it active.");
        }
        using (var watcher = DebugWatcher.WatchStaticType(typeof(HookStatics)))
        {
            watcher.isEnabled = true; var value = watcher.Values.Single(v => v.Name == nameof(HookStatics.Value));
            Check(value.TryEnablePropertyHooks(out var error), "Static property hook failed: " + error);
            HookStatics.Value = 123; Check((int)value.Value! == 123, "Static setter did not capture a value.");
        }
        Utils.FramesPerSecond = 100;
        var notifying = new NotifyingSample();
        using (var watcher = new DebugWatcher(notifying))
        {
            watcher.StartSession(); watcher.SampleNow();
            var value = watcher.Values.Single(v => v.Name == nameof(NotifyingSample.Number));
            int reads = notifying.GetterCalls;
            for (int i = 1; i <= 12; i++) notifying.Number = i;
            Check(value.Timeline.Count == 13 && (int)value.Value! == 12, "PropertyChanged missed rapid transitions.");
            Thread.Sleep(100);
            Check(notifying.GetterCalls == reads + 12, "Notifying properties were still polled between changes.");
            Check(value.ObservationMode == ValueObservationMode.ChangeNotification, "Notification source was not reported.");
            Task.Run(() => notifying.Number = 50).GetAwaiter().GetResult();
            Check((int)value.Value! == 50, "Worker-thread changes were not captured immediately.");
        }
        int afterDispose = notifying.GetterCalls; notifying.Number = 77;
        Check(notifying.GetterCalls == afterDispose, "Disposing a watcher retained its property notification handler.");
        Utils.Profile = Utils.DebugProfile.Fast;
    }
    static void SnapshotSelection()
    {
        int retention = ValueTimeline.MaximumSnapshotsPerValue;
        try
        {
            ValueTimeline.MaximumSnapshotsPerValue = 32;
            var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var timeline = new ValueTimeline();
            for (int i = 0; i < 32; i++) timeline.Add(i, time.AddSeconds(i));
            var view = new HistorySelection { Timeline = timeline, Lower = .25, Upper = .75 };
            var frozen = view.Read(time.AddSeconds(32));
            Check(frozen.IsSnapshot && frozen.Samples.Select(sample => (int)sample.Value!).SequenceEqual(Enumerable.Range(8, 16)), "Max did not capture the selected historical range.");
            for (int i = 32; i < 128; i++) timeline.Add(i, time.AddSeconds(i));
            var later = view.Read(time.AddSeconds(200));
            Check(later.Samples.SequenceEqual(frozen.Samples) && later.EndTime == frozen.EndTime, "Snapshot samples or the time axis changed after live retention rolled over.");
            view.Upper = .875;
            Check((int)view.Read(time.AddSeconds(201)).Samples[^1].Value! == 27, "Adjusting Max inside a snapshot loaded new live history.");
            view.Lower = .5;
            Check((int)view.Read(time.AddSeconds(202)).Samples[0].Value! == 16, "Adjusting Min lost the captured history's coordinates.");
            view.Upper = 1;
            var live = view.Read(time.AddSeconds(203));
            Check(!live.IsSnapshot && (int)live.Samples[^1].Value! == 127 && live.EndTime == time.AddSeconds(203), "Max at 100% did not resume live updates.");
            view.Upper = .9999;
            var nearEnd = view.Read(time.AddSeconds(204));
            Check(nearEnd.IsSnapshot && view.Read(time.AddSeconds(220)).EndTime == nearEnd.EndTime, "A Max just below 100% kept advancing the clock.");
            view.Lower = view.Upper;
            Check(view.Read(time.AddSeconds(221)).Samples.Length == 1, "A collapsed range showed samples beyond its endpoint.");
            var other = new ValueTimeline(); other.Add(1000, time);
            view.Timeline = other;
            Check((int)view.Read(time.AddSeconds(222)).Samples.Single().Value! == 1000, "Selecting another value reused the previous value's frozen history.");
            var empty = new ValueTimeline(); view.Timeline = empty;
            empty.Add(2000, time);
            Check(view.Read(time.AddSeconds(223)).Samples.Length == 0, "An empty captured snapshot admitted newly recorded values.");
            view.Upper = 1;
            Check((int)view.Read(time.AddSeconds(224)).Samples.Single().Value! == 2000, "Resuming an empty snapshot did not show new data.");
        }
        finally { ValueTimeline.MaximumSnapshotsPerValue = retention; }
    }
    static void ExpandedValues()
    {
        var sample = new Sample();
        using var watcher = new DebugWatcher(sample);
        var point = watcher.Values.Single(value => value.Name == nameof(Sample.Point));
        point.Sample(DateTime.UtcNow);
        using var child = watcher.Expand(point)!;
        child.isEnabled = true; child.SampleNow();
        var x = child.Values.Single(value => value.Name == "X");
        for (int i = 1; i <= 6; i++)
        {
            sample.Point = new(10 + i, 20 + i); point.Sample(DateTime.UtcNow);
            Check(ReferenceEquals(watcher.Expand(point), child), "A new boxed struct replaced the expanded watcher.");
            child.SampleNow();
            Check(ReferenceEquals(child.Values.Single(value => value.Name == "X"), x) && (float)x.Value! == 10 + i, "An expanded component lost its identity or stopped following its parent.");
        }
        Check(x.Timeline.Count == 7, "Expanded component history was cleared between parent updates.");
        sample.Point = new(50, 95); point.Sample(DateTime.UtcNow);
        x.ApplyAsync("75").GetAwaiter().GetResult();
        Check(sample.Point == new Vector2(75, 95), "Editing X overwrote the parent's newer Y component.");
        Check((Vector2)point.Timeline.GetSnapshots()[0].Value! == new Vector2(10, 20), "Editing a component mutated a historical boxed snapshot.");
    }
    static void Runtime(string result)
    {
        using var app = new NativeWindow("Project-Z debugger acceptance target", 1100, 720);
        DebugWatcher? watcher = null; DebugWindow? inspector = null;
        var sample = new Sample(); int frames = 0; bool editQueued = false; Task? editTask = null;
        int stableTreeFrames = 0;
        string? frozenPlotHash = null;
        int samplesAtFreeze = 0;
        int glowStage = 0, restartFrame = 0, peakGreen = 0, fadingGreen = 0;
        long glowStarted = 0;
        float peakOpacity = 0, fadingOpacity = 0;
        DebugWatcher? pointChild = null;
        DebugValue? selectedComponent = null;
        UpdatingTreeText IdleRow() => (UpdatingTreeText)inspector!.TreeRows.Single(row => row.Text.Contains("IdleValue:"));
        int GreenPixels(string suffix)
        {
            string path = result + suffix + ".png"; inspector!.SaveFrame(path);
            using var image = new System.Drawing.Bitmap(path);
            var row = IdleRow(); int green = 0;
            for (int y = (int)row.Position.Y - 3; y < row.Position.Y + row.Size.Y + 3; y++)
                for (int x = (int)row.Position.X; x < row.Position.X + row.Size.X; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    if (pixel.G > 30 && pixel.G > pixel.R + 3 && pixel.G > pixel.B + 3) green++;
                }
            return green;
        }
        string PlotHash(string suffix)
        {
            var path = result + suffix + ".png"; inspector!.SaveFrame(path);
            using var image = new System.Drawing.Bitmap(path);
            var plot = inspector.Plot;
            using var crop = image.Clone(new System.Drawing.Rectangle((int)plot.Position.X, (int)plot.Position.Y, (int)plot.Size.X, (int)plot.Size.Y), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var bytes = new MemoryStream(); crop.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray()));
        }
        app.Ready += () =>
        {
            app.UseScriptedInput();
            app.Label("Project-Z native debugger acceptance", 30, 25, 1000, 20);
            watcher = new(sample); watcher.AttachScene(app.Scene); watcher.StartSession();
            RootDiscovery.Register(app.Scene);
        };
        app.Frame += () =>
        {
            frames++; sample.Number = 10 + Math.Sin(frames / 15d) * 5;
            if (frames == 10)
            {
                inspector = new DebugWindow(); inspector.Show();
                inspector.Surface.UseScriptedInput();
                inspector.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Number)));
            }
            if (frames == 30 && !editQueued)
            {
                editQueued = true;
                // Exercise marshaling from a foreign thread into the scene loop.
                editTask = Task.Run(() => watcher!.Values.Single(v => v.Name == nameof(Sample.Text)).ApplyAsync("scene-thread write"));
            }
            if (frames >= 45 && frames < 69)
            {
                var capture = result + ".tree-frame.png";
                inspector!.SaveFrame(capture);
                using var image = new System.Drawing.Bitmap(capture);
                int textPixels = 0;
                for (int y = 104; y < Math.Min(image.Height - 140, 420); y += 2)
                    for (int x = 12; x < 305; x += 2)
                    {
                        var pixel = image.GetPixel(x, y);
                        if (pixel.R > 90 && pixel.G > 90 && pixel.B > 90) textPixels++;
                    }
                Check(textPixels >= 24, $"Watcher tree disappeared during refresh at frame {frames}: {textPixels} foreground pixels.");
                int bleedPixels = 0;
                for (int y = 104; y < Math.Min(image.Height - 140, 420); y++)
                    for (int x = 330; x < 345; x++)
                    {
                        var pixel = image.GetPixel(x, y);
                        if (pixel.R > 90 && pixel.G > 90 && pixel.B > 90) bleedPixels++;
                    }
                Check(bleedPixels == 0, $"Watcher text escaped into the plot gutter at frame {frames}: {bleedPixels} foreground pixels.");
                stableTreeFrames++;
            }
            if (frames == 70)
            {
                Check(inspector!.Plot.Labels.Any(label => label.StartsWith("Value ")) && inspector.Plot.Labels.Any(label => label.StartsWith("+")), "Numeric value or time axis labels are missing.");
                inspector!.DispatchClick(380, 75);
                Check(inspector.VisualizationMode == VisualizationMode.TimeSeries, "Native View button did not receive its click.");
            }
            if (frames == 72)
            {
                var range = inspector!.HistoryRange;
                int x = (int)range.Position.X + 10, y = (int)(range.Position.Y + range.Size.Y / 2), width = (int)range.Size.X - 20;
                inspector.DispatchDrag(x, y, x + width * 30 / 100, y);
                Check(Math.Abs(range.LowerValue - 30) < 1 && range.UpperValue == 100, "Dragging the minimum handle changed the wrong endpoint.");
                inspector.DispatchDrag(x + width, y, x + width * 80 / 100, y);
                Check(Math.Abs(range.UpperValue - 80) < 1, "Dragging the maximum handle failed.");
                double oldUpper = range.UpperValue; inspector.DispatchKey(Microsoft.Xna.Framework.Input.Keys.Right);
                Check(range.UpperValue > oldUpper, "The range slider did not respond to keyboard adjustment.");
                double oldLower = range.LowerValue, span = range.UpperValue - range.LowerValue;
                inspector.DispatchDrag(x + width / 2, y, x + width * 55 / 100, y);
                Check(range.LowerValue > oldLower && Math.Abs(range.UpperValue - range.LowerValue - span) < .01, "Dragging the selected interval changed its width.");
                void TypeRange(ProjectZ.Shared.Drawing.UI.Input.Textbox input, string text)
                {
                    inspector.DispatchClick((int)input.Position.X + 10, (int)input.Position.Y + 12);
                    inspector.DispatchKey(Microsoft.Xna.Framework.Input.Keys.End);
                    for (int i = input.Text.Length; i > 0; i--) inspector.DispatchKey(Microsoft.Xna.Framework.Input.Keys.Back);
                    inspector.Surface.DispatchText(text);
                }
                TypeRange(inspector.HistoryMinimumInput, "35"); TypeRange(inspector.HistoryMaximumInput, "75");
                inspector.DispatchKey(Microsoft.Xna.Framework.Input.Keys.Enter);
                Check(inspector.HistoryRange.isSelected, "Enter did not commit the range editor and focus the slider.");
                Check(Math.Abs(inspector.Plot.Lower - .35) < .0001 && Math.Abs(inspector.Plot.Upper - .75) < .0001, "Typing Min/Max did not update the selected graph interval.");
                var spelling = inspector.ValueInput.GetType().GetProperty("IsSpellCheckEnabled");
                Check(spelling is null || Equals(spelling.GetValue(inspector.ValueInput), false), "Value editor spelling was enabled.");
            }
            if (frames == 74)
            {
                Check(inspector!.TreeRows.Single(row => row.Text.Contains("Number:")).Text.StartsWith("  "), "Scalar tree leaves still show an expansion control.");
                var itemsRow = inspector.TreeRows.Single(row => row.Text.Contains("Items:"));
                Check(itemsRow.Text.StartsWith("▸"), "Closed tree item did not show a closed indicator.");
                inspector.DispatchClick((int)itemsRow.Position.X + 5, (int)itemsRow.Position.Y + 8);
                Check(inspector.TreeRows.Single(row => row.Text.Contains("Items:")).Text.StartsWith("▾") && inspector.TreeRows.Any(row => row.Text.Contains("[0]:")), "Tree expansion did not update the indicator and children.");
            }
            if (frames == 76)
            {
                var itemsRow = inspector!.TreeRows.Single(row => row.Text.Contains("Items:"));
                inspector.DispatchClick((int)itemsRow.Position.X + 5, (int)itemsRow.Position.Y + 8);
                Check(inspector.TreeRows.Single(row => row.Text.Contains("Items:")).Text.StartsWith("▸") && !inspector.TreeRows.Any(row => row.Text.Contains("[0]:")), "Tree collapse left stale children or the wrong indicator.");
                var root = inspector.TreeRows[0]; inspector.DispatchClick((int)root.Position.X + 5, (int)root.Position.Y + 8);
                Check(inspector.TreeRows.Count == 1 && inspector.TreeRows[0].Text.StartsWith("▸"), "Root watcher did not collapse.");
                inspector.DispatchClick((int)root.Position.X + 5, (int)root.Position.Y + 8);
                Check(inspector.TreeRows.Count > 1 && inspector.TreeRows[0].Text.StartsWith("▾"), "Root watcher did not reopen.");
                inspector.Select(watcher!, watcher!.Values.Single(value => value.Name == nameof(Sample.Text)));
                var button = inspector.PropertyHookButton;
                inspector.DispatchClick((int)button.Position.X + 15, (int)button.Position.Y + 12);
                var selected = inspector.SelectedValue!;
                Check(selected.ObservationMode == ValueObservationMode.PropertyHook, "The native property hook control did not enable accessors.");
                for (int i = 0; i < 10; i++) sample.Text = "Native hook transition " + i;
                Check((string)selected.Value! == "Native hook transition 9" && selected.Timeline.Count >= 11, "The native property hook missed changes within one frame.");
                inspector.SaveFrame(result + ".controls.png");
                inspector.DispatchClick((int)button.Position.X + 15, (int)button.Position.Y + 12);
                Check(selected.ObservationMode == ValueObservationMode.Polling, "The native unhook control did not remove accessors.");
                // Preserve the scene-thread edit assertion later in this test.
                sample.Text = "scene-thread write";
                inspector.HistoryRange.SetRange(0, 100);
            }
            if (frames == 80)
            {
                inspector!.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Number)));
                inspector.HistoryRange.UpperValue = 75;
                samplesAtFreeze = inspector.SelectedValue!.Timeline.Count;
            }
            if (frames == 82) frozenPlotHash = PlotHash(".snapshot-before");
            if (frames == 84)
            {
                Check(inspector!.SelectedValue!.Timeline.Count > samplesAtFreeze, "Background recording stopped when Max was changed.");
                Check(inspector.Plot.IsSnapshot && inspector.Plot.Summary.StartsWith("Snapshot"), "The frozen graph did not identify snapshot mode.");
                Check(PlotHash(".snapshot-after") == frozenPlotHash, "The snapshot graph changed while new live samples arrived.");
                inspector.HistoryRange.UpperValue = 100;
            }
            if (frames == 86) Check(PlotHash(".resumed") != frozenPlotHash, "Returning Max to 100% did not resume the live graph.");
            if (frames == 90) inspector!.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Boolean)));
            if (frames == 92)
            {
                Check(IdleRow().GlowOpacity == 0, "An unchanged tree value acquired a glow from row reuse or selection.");
                sample.IdleValue = 1; watcher!.Values.Single(value => value.Name == nameof(Sample.IdleValue)).Sample(DateTime.UtcNow);
                glowStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            }
            if (frames == 93)
            {
                peakOpacity = IdleRow().GlowOpacity; peakGreen = GreenPixels(".glow-peak");
                Check(peakOpacity > 0 && peakGreen > 10, "A recorded tree update did not render a green text underglow.");
                glowStage = 1;
            }
            if (glowStage == 1 && System.Diagnostics.Stopwatch.GetElapsedTime(glowStarted).TotalSeconds >= UpdatingTreeText.FadeSeconds * .3)
            {
                fadingOpacity = IdleRow().GlowOpacity; fadingGreen = GreenPixels(".glow-fading");
                Check(fadingOpacity > 0 && fadingOpacity < peakOpacity && fadingGreen > 0 && fadingGreen < peakGreen, "The green text underglow did not fade gradually between updates.");
                sample.IdleValue = 2; watcher!.Values.Single(value => value.Name == nameof(Sample.IdleValue)).Sample(DateTime.UtcNow);
                glowStarted = System.Diagnostics.Stopwatch.GetTimestamp(); restartFrame = frames + 1; glowStage = 2;
            }
            if (glowStage == 2 && frames == restartFrame)
            {
                Check(IdleRow().GlowOpacity > fadingOpacity && GreenPixels(".glow-restarted") > fadingGreen, "A new update did not restart the underglow fade.");
                glowStage = 3;
            }
            if (glowStage == 3 && System.Diagnostics.Stopwatch.GetElapsedTime(glowStarted).TotalSeconds >= UpdatingTreeText.FadeSeconds + .15)
            {
                Check(IdleRow().GlowOpacity == 0 && GreenPixels(".glow-transparent") == 0, "The underglow did not return fully to transparent.");
                glowStage = 4;
            }
            if (frames == 95) inspector!.SaveFrame(result + ".boolean.png");
            if (frames == 100) inspector!.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Point)));
            if (frames == 105) inspector!.DispatchClick(380, 75);
            if (frames == 115) inspector!.SaveFrame(result + ".spatial.png");
            if (frames == 120) { inspector!.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Tint))); inspector.DispatchClick(380, 75); }
            if (frames == 130) inspector!.SaveFrame(result + ".events.png");
            if (frames == 135) { inspector!.Select(watcher!, watcher!.Values.Single(v => v.Name == nameof(Sample.Number))); inspector.DispatchClick(380, 75); }
            if (frames >= 140 && frames <= 175) sample.Point = new(frames, frames * 2);
            if (frames == 140)
            {
                var row = inspector!.TreeRows.Single(row => row.Text.Contains("Point:"));
                inspector.DispatchClick((int)row.Position.X + 5, (int)row.Position.Y + 8);
                var point = watcher!.Values.Single(value => value.Name == nameof(Sample.Point));
                pointChild = watcher.Children.Single(child => child.ParentValueGuid == point.Guid);
                selectedComponent = pointChild.Values.Single(value => value.Name == "X");
                Check(selectedComponent.Value is float, "Expanding a struct exposed an uninitialized null component.");
            }
            if (frames == 142)
            {
                var row = inspector!.TreeRows.Single(row => row.Text.TrimStart().StartsWith("X:"));
                inspector.DispatchClick((int)row.Position.X + 32, (int)row.Position.Y + 8);
                Check(ReferenceEquals(inspector.SelectedValue, selectedComponent), "Clicking X selected the wrong child value.");
            }
            if (frames == 178)
            {
                Check(ReferenceEquals(inspector!.SelectedValue, selectedComponent) && ReferenceEquals(watcher!.Children.Single(child => child.ParentValueGuid == pointChild!.ParentValueGuid), pointChild), "Updating the parent replaced the selected child watcher.");
                Check(selectedComponent!.Value is float x && x >= 170 && selectedComponent.Timeline.Count > 5, "Selected X stopped updating or lost its numeric history.");
                Check(inspector.Plot.Timeline == selectedComponent.Timeline && inspector.Plot.Labels.Any(label => label.StartsWith("Value ")), "Clicking X did not keep a numeric graph.");
                inspector.SaveFrame(result + ".selected-component.png");
            }
            if (frames > 180 && glowStage == 4)
            {
                Check(inspector?.IsVisible == true, "Native debugger did not remain visible.");
                Check(stableTreeFrames == 24, "Sustained watcher tree rendering was not checked.");
                Check(editTask?.IsCompletedSuccessfully == true && sample.Text == "scene-thread write", "Scene-thread edit did not complete.");
                Check(watcher!.Values.Single(v => v.Name == nameof(Sample.Number)).Timeline.GetSnapshots().Length > 2, "Live native scene did not sample changing values.");
                Check(global::ProjectZ.Shared.Drawing.UI.SceneElement.RenderExceptionCount == 0, global::ProjectZ.Shared.Drawing.UI.SceneElement.FirstRenderException ?? "Native rendering failed.");
                var hwnd = global::ProjectZ.Windows.Composition.WindowsComposition.ResolveHwnd(app);
                Check(hwnd != IntPtr.Zero, "No real HWND was created.");
                inspector!.SaveFrame(result + ".png");
                File.WriteAllText(result, $"PASS native HWND, selected expanded X stays numeric with stable selection and accumulated history, green tree text underglow ({peakGreen} to {fadingGreen} to 0 pixels), fade restart on update, no false pulse on unchanged values or reused rows, frozen graph pixel stability while recording, Max=100 live resume, numeric/time axes, dual-thumb drag/pan/keyboard/Min-Max input, spelling disabled, tree open/closed/leaf states, property hook UI and same-frame changes, scene-thread editing, 24 consecutive watcher-tree frames without blanking or text bleed");
                inspector!.Dispose(); watcher.Dispose(); app.Exit();
            }
        };
        app.Run();
    }
    static void ProtectedRuntime(string executable, string result)
    {
        var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(executable)! };
        // An unprotected .NET host fails before Main when this hook is missing.
        start.Environment["DOTNET_STARTUP_HOOKS"] = Path.Combine(Path.GetDirectoryName(executable)!, "deliberately-missing-hook.dll");
        using var process = System.Diagnostics.Process.Start(start)!;
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20); bool ready = false;
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                process.Refresh();
                try { ready = process.Modules.Cast<System.Diagnostics.ProcessModule>().Any(m => string.Equals(m.ModuleName, "ProjectZ.dll", StringComparison.OrdinalIgnoreCase)) && process.Modules.Cast<System.Diagnostics.ProcessModule>().Any(m => string.Equals(m.ModuleName, "mgruntime.dll", StringComparison.OrdinalIgnoreCase)); } catch (System.ComponentModel.Win32Exception) { }
                if (ready) break; Thread.Sleep(100);
            }
            Check(ready && !process.HasExited, "Protected Project-Z target did not load its native renderer with a blocked startup hook.");
            Thread.Sleep(1000); Check(!process.HasExited, "Protected target exited after renderer startup.");
            File.WriteAllText(result, "PASS protected Project-Z deployment runs, native renderer loads, and hostile startup-hook environment is blocked");
        }
        finally { if (!process.HasExited) { process.CloseMainWindow(); if (!process.WaitForExit(3000)) process.Kill(); } }
    }
}
