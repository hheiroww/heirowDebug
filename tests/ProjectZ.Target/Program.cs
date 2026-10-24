using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectZ.Shared.Content;
using ProjectZ.Shared.Drawing;
using ProjectZ.Shared.Drawing.UI.Primitives;
using System.Collections;
using System.Reflection;

// Deliberately has NO debugger reference. Injection must find a live application.
static class Program
{
    [STAThread] static void Main(string[] args) { using var game = new TargetGame(args is ["--observe-result", var path] ? path : null); game.Run(); }
}
// Keep the manager private in a base class, as a host wrapper or weaver may do.
abstract class TargetGameBase : Game
{
    private SceneManager? manager;
    protected SceneManager Manager { get => manager!; set => manager = value; }
}
sealed class TargetGame : TargetGameBase
{
    readonly GraphicsDeviceManager graphics;
    readonly string? observationResult;
    bool observationComplete;
    public double Number
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] get;
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] set;
    }
    public bool Active { get; set; }
    public Vector2 Position { get; set; }
    public TargetGame(string? observationResult = null)
    {
        this.observationResult = observationResult;
        graphics = new(this) { PreferredBackBufferWidth = 720, PreferredBackBufferHeight = 420, GraphicsProfile = GraphicsProfile.HiDef };
        Window.Title = "Project-Z unreferenced injection target"; IsMouseVisible = true;
    }
    sealed class TargetScene(SceneManager manager) : Scene(manager);
    protected override void LoadContent()
    {
        var batch = new SpriteBatch(GraphicsDevice); var content = new ContentContainer(Content, GraphicsDevice); content.LoadVectorFontsOnly();
        Services.AddService(typeof(SpriteBatch), batch); Services.AddService(typeof(GraphicsDevice), GraphicsDevice); Services.AddService(typeof(ContentContainer), content);
        Manager = new(this, 60); var scene = new TargetScene(Manager) { BackgroundColor = new Microsoft.Xna.Framework.Color(20, 35, 45), EnableDesignTimeControls = false };
        scene.AddElement(new TextElement(scene) { Text = "Project-Z target – no debugger package reference", Position = new(25, 25), Size = new(660, 60), ForegroundColor = Microsoft.Xna.Framework.Color.White });
        Manager.AddScene("main", scene); Manager.ActiveScene = scene;
    }
    protected override void Update(GameTime time)
    {
        Number = 30 + Math.Sin(time.TotalGameTime.TotalSeconds) * 20; Active = (int)time.TotalGameTime.TotalSeconds % 2 == 0;
        Position = new((float)Number, (float)(Number * 2)); Manager.Tick(time); base.Update(time);
        if (observationResult is not null && !observationComplete) ProbeObservation();
    }
    void ProbeObservation()
    {
        // Find the injected API through reflection: this target still has no
        // debugger/Harmony reference and neither DLL exists in its deployment.
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "heirowDebug.ProjectZ");
        if (assembly is null) return;
        var watcherType = assembly.GetType("heirowDebug.ProjectZ.DebugWatcher")!;
        var registry = (IEnumerable)watcherType.GetProperty("Watchers")!.GetValue(null)!;
        var watcher = registry.Cast<object>().Select(pair => pair.GetType().GetProperty("Value")!.GetValue(pair)!)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.GetType().GetProperty("AttachedObject")!.GetValue(candidate), this));
        if (watcher is null || watcherType.GetProperty("DebugWindow")!.GetValue(null) is not { } inspector) return;
        observationComplete = true;
        try
        {
            var values = (IEnumerable)watcherType.GetProperty("Values")!.GetValue(watcher)!;
            var value = values.Cast<object>().Single(item => (string)item.GetType().GetProperty("Name")!.GetValue(item)! == nameof(Number));
            var valueType = value.GetType(); object?[] arguments = [null];
            if (!(bool)valueType.GetMethod("TryEnablePropertyHooks")!.Invoke(value, arguments)!) throw new InvalidOperationException("Property hook failed: " + arguments[0]);
            var timeline = valueType.GetProperty("Timeline")!.GetValue(value)!;
            int before = (int)timeline.GetType().GetProperty("Count")!.GetValue(timeline)!;
            for (int i = 0; i < 20; i++) Number = 1000 + i;
            int captured = (int)timeline.GetType().GetProperty("Count")!.GetValue(timeline)! - before;
            if ((double)valueType.GetProperty("Value")!.GetValue(value)! != 1019 || captured < 20) throw new InvalidOperationException("Injected hooks missed same-frame transitions.");
            var editor = inspector.GetType().GetProperty("ValueInput", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(inspector)!;
            var spelling = editor.GetType().GetProperty("IsSpellCheckEnabled");
            if (spelling is not null && (bool)spelling.GetValue(editor)!) throw new InvalidOperationException("Spelling was enabled in the injected editor.");
            File.WriteAllText(observationResult!, System.Text.Json.JsonSerializer.Serialize(new
            {
                result = "PASS injected accessor hooks without a debugger dependency in the target",
                capturedTransitions = captured, harmonyLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "0Harmony"),
                spellCheckAvailable = spelling is not null, spellCheckEnabled = false
            }));
        }
        catch (Exception ex) { File.WriteAllText(observationResult! + ".error", ex.ToString()); }
        Exit();
    }
    protected override void Draw(GameTime time) { Manager.Draw(time); base.Draw(time); }
    protected override void Dispose(bool disposing) { if (disposing) Manager?.Dispose(); base.Dispose(disposing); }
}
