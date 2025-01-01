Public Class ArrayCompareResults
    Public Property ChangedIndexies As Integer() = New Integer() {}

    Public Property ValueChanged As Boolean
    Public Sub New()

    End Sub

    Public Sub New(ChangedIndexies As Integer(), ValueChanged As Boolean)
        Me.ChangedIndexies = ChangedIndexies
        Me.ValueChanged = ValueChanged
    End Sub
End Class