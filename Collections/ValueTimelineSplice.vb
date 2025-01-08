Imports JackDebug.WPF.Values

Namespace Collections
    Public Class ValueTimelineSplice

        Implements IDisposable

#Region "Properties"

        Public Property Values As List(Of DebugValue)
            Get
                Return _Values
            End Get
            Set(value As List(Of DebugValue))
                _Values = value
            End Set
        End Property
        Private _Values As New List(Of DebugValue)

        Public ReadOnly Property isGraphable As Boolean
            Get
                Return _isGraphable
            End Get
        End Property
        Private _isGraphable As Boolean
#End Region

#Region "IDisposable"
        Private disposedValue As Boolean
        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    If NotNothing(_Values) Then
                        _Values.Clear()
                        _Values = Nothing
                    End If
                End If

                disposedValue = True
            End If
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub
#End Region

        Public Function Clone() As ValueTimelineSplice
            Dim c As ValueTimelineSplice = CloneObject(Of ValueTimelineSplice)
            Return c
        End Function

        Public Sub New(Values As List(Of DebugValue))
            _Values = Values
            _isGraphable = Values.Length > 0 AndAlso Values.First().Flags.isGraphable()
        End Sub

    End Class
End Namespace