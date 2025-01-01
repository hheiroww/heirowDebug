Imports MicroSerializationLibrary.Serialization

Public Class FieldReferenceInstance
    Inherits FieldReference

    Public Sub New(Instance As Object, FieldInfo As Reflection.FieldInfo, Index As Integer, i As Integer)
        MyBase.New(Instance, FieldInfo, Index)
    End Sub
End Class
