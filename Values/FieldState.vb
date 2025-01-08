Imports MicroSerializationLibrary.Serialization

Public Class FieldState

    Public Sub New(Index As Integer, f As FieldReference)
        Me.Index = Index
        Me.f = f
    End Sub

    Public Property Index As Integer = -1
    Public Property f As FieldReference

End Class