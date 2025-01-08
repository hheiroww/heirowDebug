Imports MicroSerializationLibrary.Serialization

Public Class PropertyState

    Public Sub New(Index As Integer, p As PropertyReference)
        Me.Index = Index
        Me.p = p
    End Sub

    Public Property Index As Integer = -1
    Public Property p As PropertyReference

End Class