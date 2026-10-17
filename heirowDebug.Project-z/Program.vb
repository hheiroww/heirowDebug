#Region "Using Statements"
Imports System.Collections.Generic
Imports System.Linq
#End Region

#If WINDOWS OrElse LINUX Then
''' <summary>
''' The main class.
''' </summary>
Public NotInheritable Class Program
	Private Sub New()
	End Sub
	''' <summary>
	''' The main entry point for the application.
	''' </summary>
	<STAThread> _
	Friend Shared Sub Main()
        Using game As New GameWindow()
            game.Run()
        End Using
	End Sub
End Class
#End If
