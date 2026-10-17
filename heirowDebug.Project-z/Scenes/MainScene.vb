#Region "Using Statements"
Imports Microsoft.Xna.Framework
Imports Microsoft.Xna.Framework.Graphics
Imports ProjectZ.Shared.Drawing

#End Region

''' <summary>
''' Main Application Scene.
''' Add your game elements here.
''' </summary>
Public Class MainScene
    Inherits Scene

    ''' <summary>
    ''' Initialization of the Scene.
    ''' </summary>
    ''' <remarks>Elements can be added here.</remarks>
    Public Sub New(ByRef SceneManager As SceneManager)
        MyBase.New(SceneManager)
        Me.isCursorVisible = True
        Me.BackgroundColor = New Color(30, 30, 30)

        ' TODO: Add your SceneElements here using AddElement()
    End Sub

    ''' <summary>
    ''' Here is where you can add Initialization logic to your Scene.
    ''' </summary>
    ''' <remarks>This is most useful for classes that require object instances to be constructed.</remarks>
    Public Overrides Sub Initialize(gameTime As GameTime)
        MyBase.Initialize(gameTime)

        isCursorVisible = True
        Me.sender.IsMouseVisible = True
    End Sub

End Class
