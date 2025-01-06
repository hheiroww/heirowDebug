Namespace Values
    Public Class DebugValueUpdate
        Implements IDisposable

#Region "IDisposable"
        Private disposedValue As Boolean
        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    LastValue.Dispose()
                    CurrentValue.Dispose()
                    _LastValue = Nothing
                    _CurrentValue = Nothing
                End If

                disposedValue = True
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub
#End Region

#Region "Properties"
        Public ReadOnly Property ValueChanged As Boolean
            Get
                Return _ValueChanged
            End Get
        End Property
        Private _ValueChanged As Boolean = False

        Public ReadOnly Property LastValue As DebugValue
            Get
                Return _LastValue
            End Get
        End Property
        Private _LastValue As DebugValue

        Public ReadOnly Property CurrentValue As DebugValue
            Get
                Return _CurrentValue
            End Get
        End Property
        Private _CurrentValue As DebugValue

#End Region

        Public Sub New(LastValue As DebugValue, CurrentValue As DebugValue, ValueChanged As Boolean)
            _LastValue = LastValue
            _CurrentValue = CurrentValue
            _ValueChanged = ValueChanged
        End Sub

    End Class
End Namespace