using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ProjectZ.Shared.Content;
using ProjectZ.Shared.Drawing;
using ProjectZ.Shared.Drawing.UI.Primitives;

// Deliberately has NO debugger reference. Injection must find a live application.
static class Program
{
    public static TargetGame? Game;
    [STAThread] static void Main() { using (Game = new TargetGame()) Game.Run(); }
}
sealed class TargetGame : Game
{
    readonly GraphicsDeviceManager graphics;
    public SceneManager Manager = null!;
    public double Number { get; set; }
    public bool Active { get; set; }
    public Vector2 Position { get; set; }
    public TargetGame()
    {
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
    }
    protected override void Draw(GameTime time) { Manager.Draw(time); base.Draw(time); }
    protected override void Dispose(bool disposing) { if (disposing) Manager?.Dispose(); base.Dispose(disposing); }
}
