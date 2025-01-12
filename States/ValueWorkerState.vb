Namespace States

    Public Class ValueWorkerState
        Public Sub New(Type As ReferenceType)
            _Type = Type
        End Sub

        Public Property Reference
        Public Property Instance As Object
        Public ReadOnly Property Type As ReferenceType
            Get
                Return _Type
            End Get
        End Property
        Private _Type As ReferenceType
        Public Property Watcher As DebugWatcher
    End Class

    Public Enum ReferenceType
        [Property]
        [Field]
    End Enum

End Namespace