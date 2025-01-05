Namespace Values
    Public Class ArrayCompareResults
        Public Property ChangedIndexies As New List(Of Integer)

        Public Property ValueChanged As Boolean
        Public Sub New()

        End Sub

        Public Sub New(ChangedIndexies As List(Of Integer), ValueChanged As Boolean)
            Me.ChangedIndexies = ChangedIndexies
            Me.ValueChanged = ValueChanged
        End Sub
    End Class
End Namespace