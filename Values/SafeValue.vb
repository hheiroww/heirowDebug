Namespace Values
    Public Class SafeValue
        Implements IDisposable

        Public ReadOnly Property Value As Object
            Get
                Return _Value
            End Get
        End Property
        Private _Value As Object

        Public ReadOnly Property IsNothing As Boolean
            Get
                Return _IsNothing
            End Get
        End Property
        Private _IsNothing As Boolean = False

        Public Sub New(Value As Object)
            If Utils.IsNothing(Value) Then
                _Value = Nothing
                _IsNothing = True
            ElseIf DebugWatcher.IgnoreTypes.Contains(Value.GetType) Then
                _Value = Nothing
                _IsNothing = True
            Else
                _Value = Value
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            _Value = Nothing
            _IsNothing = Nothing
        End Sub
    End Class
End Namespace