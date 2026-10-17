#Region "Using Statements"
Imports Microsoft.Xna.Framework
Imports Microsoft.Xna.Framework.Graphics
Imports ProjectZ.Shared.Content
Imports ProjectZ.Shared.Drawing

#End Region

''' <summary>
''' !!! IMPORTANT NOTE !!!
''' DO NOT EDIT THIS CLASS UNLESS YOU KNOW WHAT YOU ARE DOING.
''' </summary>
''' <remarks></remarks>
Partial Public Class GameWindow
    Inherits Game

#Region "Properties"

    Public Property Size As New Point(800, 600)

#End Region

#Region "Internals"

    Public graphics As GraphicsDeviceManager
    Public spriteBatch As SpriteBatch
    Public contentCollection As ContentContainer
    Public sceneManager As SceneManager

#End Region

    Protected Overrides Sub Draw(gameTime As GameTime)

        ' Draw the current Scene
        sceneManager.Draw(gameTime)

        MyBase.Draw(gameTime)

    End Sub

    Protected Overrides Sub LoadContent()

        ' Create the Sprite Batch
        spriteBatch = New SpriteBatch(GraphicsDevice)

        ' Initialize the Content Container
        contentCollection = New ContentContainer(Content, GraphicsDevice)

        ' Load Content
        contentCollection.LoadAllContent()

        ' Add our services to access them from another class instance
        Services.AddService(GetType(SpriteBatch), spriteBatch)
        Services.AddService(GetType(GraphicsDevice), GraphicsDevice)
        Services.AddService(GetType(ContentContainer), contentCollection)

        SetupSceneManager()
    End Sub

    Protected Overrides Sub Update(gameTime As GameTime)
        MyBase.Update(gameTime)
        sceneManager.Tick(gameTime)
    End Sub

    Protected Overrides Sub Initialize()
        MyBase.Initialize()
    End Sub

End Class
