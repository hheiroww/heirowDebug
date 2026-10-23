using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectZ.Shared.Content;
using ProjectZ.Shared.Drawing;
using ProjectZ.Shared.Drawing.UI;
using ProjectZ.Shared.Drawing.UI.Input;
using ProjectZ.Shared.Drawing.UI.Primitives;
using NativeButton = ProjectZ.Shared.Drawing.UI.Input.Button;

namespace heirowDebug.ProjectZ;

/// <summary>Common native Project-Z desktop surface for the inspector, injector and protection tools.</summary>
public class NativeWindow : Game
{
    readonly GraphicsDeviceManager graphics;
    SceneManager manager = null!;
    SpriteBatch presentation = null!;
    RenderTarget2D? frame;
    public Scene Scene { get; private set; } = null!;
    public ConcurrentQueue<Action> Pending { get; } = new();
    public event Action? Ready;
    public event Action? Frame;
    public bool Closed { get; private set; }
    public int Width => GraphicsDevice.Viewport.Width;
    public int Height => GraphicsDevice.Viewport.Height;
    public NativeWindow(string title, int width = 1280, int height = 800)
    {
        Window.Title = title; Window.AllowUserResizing = true; IsMouseVisible = true;
        IsFixedTimeStep = false; InactiveSleepTime = TimeSpan.FromMilliseconds(16);
        graphics = new(this) { PreferredBackBufferWidth = width, PreferredBackBufferHeight = height, GraphicsProfile = GraphicsProfile.HiDef, SynchronizeWithVerticalRetrace = true };
        Exiting += (_, _) => Closed = true;
    }
    sealed class SurfaceScene(SceneManager manager) : Scene(manager);
    protected override void LoadContent()
    {
        var batch = new SpriteBatch(GraphicsDevice); var content = new ContentContainer(Content, GraphicsDevice); content.LoadVectorFontsOnly();
        presentation = new SpriteBatch(GraphicsDevice);
        Services.AddService(typeof(SpriteBatch), batch); Services.AddService(typeof(GraphicsDevice), GraphicsDevice); Services.AddService(typeof(ContentContainer), content);
        manager = new SceneManager(this, 60); Scene = new SurfaceScene(manager) { BackgroundColor = new Color(16, 22, 29), EnableDesignTimeControls = false };
        Scene.Quality.AntiAliasingSamples = 16;
        Scene.Quality.Filtering = TextureFiltering.Anisotropic16x;
        manager.AddScene("debugger", Scene); manager.ActiveScene = Scene;
        // Route each window's actual HWND messages. Process-global SDL input would
        // otherwise let the debugger steal the target scene's keyboard/pointer.
        manager.UseWindowMessagesInput = true;
        ApplyIcon(); Ready?.Invoke();
    }
    void ApplyIcon()
    {
        var hwnd = global::ProjectZ.Windows.Composition.WindowsComposition.ResolveHwnd(this);
        using var stream = OpenWindowIcon();
        if (stream is null) return;
        using var icon = new System.Drawing.Icon(stream);
        SendMessage(hwnd, 0x80, 0, icon.Handle); SendMessage(hwnd, 0x80, 1, icon.Handle);
        // Windows copies neither WM_SETICON handle; retain a clone for the window lifetime.
        retainedIcon = (System.Drawing.Icon)icon.Clone();
        SendMessage(hwnd, 0x80, 0, retainedIcon.Handle); SendMessage(hwnd, 0x80, 1, retainedIcon.Handle);
    }
    System.Drawing.Icon? retainedIcon;
    protected virtual Stream? OpenWindowIcon() => typeof(NativeWindow).Assembly.GetManifestResourceStream("heirowDebug.ProjectZ.ico");
    public bool IsWindowVisible => IsWindowVisibleNative(global::ProjectZ.Windows.Composition.WindowsComposition.ResolveHwnd(this));
    public void ShowNativeWindow()
    {
        // RunOneFrame does not enter the native platform RunLoop that shows a
        // window. Explicitly show debugger surfaces pumped by another scene.
        ShowWindow(global::ProjectZ.Windows.Composition.WindowsComposition.ResolveHwnd(this), 5);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsWindowVisible")] static extern bool IsWindowVisibleNative(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam);
    protected override void Update(GameTime time)
    {
        while (Pending.TryDequeue(out var action)) action();
        manager.Tick(time); Frame?.Invoke(); base.Update(time);
    }
    protected override void Draw(GameTime time)
    {
        var parameters = GraphicsDevice.PresentationParameters;
        if (frame is null || frame.Width != parameters.BackBufferWidth || frame.Height != parameters.BackBufferHeight)
        {
            frame?.Dispose(); frame = new RenderTarget2D(GraphicsDevice, parameters.BackBufferWidth, parameters.BackBufferHeight, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        }
        GraphicsDevice.SetRenderTarget(frame);
        try { manager.Draw(time); } finally { GraphicsDevice.SetRenderTarget(null); }
        presentation.Begin(SpriteSortMode.Deferred, BlendState.Opaque, Scene.Quality.Sampler);
        presentation.Draw(frame, new Rectangle(0, 0, parameters.BackBufferWidth, parameters.BackBufferHeight), Color.White); presentation.End();
        base.Draw(time);
    }
    public void SaveFrame(string path)
    {
        if (frame is null) throw new InvalidOperationException("The window has not rendered a frame.");
        using var file = File.Create(path); frame.SaveAsPng(file, frame.Width, frame.Height);
    }
    public void DispatchClick(int x, int y)
    {
        bool previous = manager.UseExternalInput; manager.UseExternalInput = true;
        try { manager.InjectMouseLeftDown(x, y, Width, Height); manager.InjectMouseLeftUp(x, y, Width, Height); }
        finally { manager.UseExternalInput = previous; }
    }
    public void DispatchKey(Microsoft.Xna.Framework.Input.Keys key)
    {
        bool previous = manager.UseExternalInput; manager.UseExternalInput = true;
        try { manager.InjectKeyDown(key); manager.InjectKeyUp(key); }
        finally { manager.UseExternalInput = previous; }
    }
    protected void ResizeClient(int width, int height)
    {
        graphics.PreferredBackBufferWidth = width; graphics.PreferredBackBufferHeight = height; graphics.ApplyChanges();
    }
    public T Add<T>(T element) where T : SceneElement { Scene.AddElement(element); return element; }
    public TextElement Label(string text, float x, float y, float width = 500, int size = 12) => Add(new TextElement(Scene)
    {
        Text = text, Position = new(x, y), Size = new(width, Math.Max(24, size * 2)), Font = Fonts.SegoeUI.get_GetResourceName(size), ForegroundColor = new Color(220, 230, 237), Clip = true
    });
    public NativeButton Button(string text, float x, float y, float width, Action action)
    {
        var button = Add(new NativeButton(Scene) { AutoSize = ButtonAutoSize.None, Text = text, Position = new(x, y), Size = new(width, 30), BackgroundColor = new Color(35, 50, 64), MouseOverBackgroundColor = new Color(49, 76, 92), ForegroundColor = Color.White });
        button.MouseLeftClick += _ => action(); return button;
    }
    public Textbox Input(float x, float y, float width, string text = "") => Add(new Textbox(Scene) { Text = text, Position = new(x, y), Size = new(width, 30), AcceptsReturn = false, ForegroundColor = Color.White, BackgroundColor = new Color(24, 34, 44) });
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Closed = true; manager?.Dispose(); retainedIcon?.Dispose(); frame?.Dispose(); presentation?.Dispose(); }
        base.Dispose(disposing);
    }
}
