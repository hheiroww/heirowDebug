Imports Microsoft.Xna.Framework
Imports ProjectZ.Shared.Drawing
Imports ProjectZ.Shared.Drawing.UI.Input
Imports ProjectZ.Shared.Drawing.UI.Primitives

''' <summary>A native scene demonstrating the Project-Z debugger package.</summary>
Public Class MainScene
    Inherits Scene

    Private ReadOnly telemetry As New DemoTelemetry()
    Private watcher As Global.heirowDebug.ProjectZ.DebugWatcher

    Public Sub New(ByRef manager As SceneManager)
        MyBase.New(manager)
        BackgroundColor = New Color(16, 22, 29)
        EnableDesignTimeControls = False
    End Sub

    Public Overrides Sub Initialize(time As GameTime)
        MyBase.Initialize(time)
        sender().IsMouseVisible = True
        AddElement(New TextElement(Me) With {.Position = New Vector2(30, 30), .Size = New Vector2(730, 50), .Text = "heirowDebug / Project-Z native live debugger", .ForegroundColor = Color.White})
        Dim open As New Button(Me) With {.AutoSize = ButtonAutoSize.None, .Position = New Vector2(30, 110), .Size = New Vector2(250, 40), .Text = "Open live debugger"}
        AddHandler open.MouseLeftClick, Sub(p) Global.heirowDebug.ProjectZ.DebugWatcher.CreateDebugWindow()
        AddElement(open)
        watcher = New Global.heirowDebug.ProjectZ.DebugWatcher(telemetry, False, "Live Project-Z telemetry")
        watcher.AttachScene(Me)
        watcher.StartSession()
        AddHandler FrameUpdated, Sub(frame)
                                     Dim seconds = frame.TotalGameTime.TotalSeconds
                                     telemetry.Speed = 50 + Math.Sin(seconds) * 30
                                     telemetry.Position = New Vector2(CSng(150 + Math.Cos(seconds) * 90), CSng(150 + Math.Sin(seconds) * 90))
                                     telemetry.Running = CInt(Math.Floor(seconds)) Mod 2 = 0
                                 End Sub
    End Sub

    Public Class DemoTelemetry
        Public Property Speed As Double
        Public Property Position As Vector2
        Public Property Running As Boolean
        Public Property Tint As Color = New Color(84, 218, 195)
        Public Property Values As New List(Of Integer) From {1, 2, 3}
        Public Property Status As String = "Native Project-Z scene"
    End Class
End Class
