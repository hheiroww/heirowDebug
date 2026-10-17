#Region "Using Statements"
Imports Microsoft.Xna.Framework
Imports Microsoft.Xna.Framework.Graphics
Imports ProjectZ.Shared.Drawing

#End Region

''' <summary>
''' Game Window that MonoGame will use to display your application.
''' </summary>
''' <remarks></remarks>
Partial Public Class GameWindow
    Inherits Game

    Protected Sub SetupSceneManager()
        ' Initialize the Scene Manager With Desired FPS
        sceneManager = New SceneManager(Me, 60)

        ' Create The Main Scene
        Dim Main As New MainScene(sceneManager)
        sceneManager.AddScene("Main", Main)

        ' This is how your application knows what to draw and when to draw it.
        sceneManager.ActiveScene = Main
    End Sub

    Protected Overrides Sub UnloadContent()

    End Sub

    Public Sub New()
        MyBase.New()
        Window.Title = "heirowDebug 2026 - Project-Z sample"

        ' Set XNA Window Properties
        Size = New Point(800, 600)
        Window.AllowUserResizing = False
        graphics = New GraphicsDeviceManager(Me) With {
            .PreferMultiSampling = True,
            .SynchronizeWithVerticalRetrace = True,
            .PreferredBackBufferHeight = Size.Y,
            .PreferredBackBufferWidth = Size.X,
            .GraphicsProfile = GraphicsProfile.HiDef}

        IsFixedTimeStep = True
        IsMouseVisible = True
        Content.RootDirectory = "Content"

    End Sub

End Class
