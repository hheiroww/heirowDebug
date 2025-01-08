Imports JackDebug.WPF.Values
Imports MicroSerializationLibrary.Serialization

Namespace Collections
    Public Class DebugValueCollection
        Implements IDisposable

#Region "Properties"

        Public Property ValueChanged As Boolean
            Get
                Return _ValueChanged
            End Get
            Set(value As Boolean)
                _ValueChanged = value
            End Set
        End Property
        Private _ValueChanged As Boolean

        Public Property Values As List(Of DebugValue)
            Get
                Return _Values
            End Get
            Set(value As List(Of DebugValue))
                _Values = value
            End Set
        End Property
        Private _Values As New List(Of DebugValue)

        Public ReadOnly Property Length As Integer
            Get
                Return _Values.Length
            End Get
        End Property

#End Region

#Region "Cache"
        Private Property FieldLookup As New Dictionary(Of FieldReference, Integer)
        Private Property PropertyLookup As New Dictionary(Of PropertyReference, Integer)
#End Region

#Region "IDisposable"
        Private disposedValue As Boolean
        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    FieldLookup.Clear()
                    PropertyLookup.Clear()
                    For i As Integer = 0 To _Values.Count - 1
                        _Values(i).Dispose()
                    Next
                    _Values.Clear()
                    _ValueChanged = Nothing
                End If

                disposedValue = True
            End If
        End Sub
        Public Sub Dispose() Implements IDisposable.Dispose
            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub
#End Region

        Public Sub SetChanged()
            _ValueChanged = True
        End Sub

        Public Function ContainsField(Field As FieldReference)
            Return FieldLookup.ContainsKey(Field)
        End Function

        Public Function ContainsProperty(Prop As PropertyReference)
            Return PropertyLookup.ContainsKey(Prop)
        End Function

        Public Function GetField(Field As FieldReference) As DebugValue
            If ContainsField(Field) Then
                Return Values(FieldLookup(Field))
            Else
                Return Nothing
            End If
        End Function

        Public Function GetProperty(Prop As PropertyReference)
            If ContainsProperty(Prop) Then
                Return Values(PropertyLookup(Prop))
            Else
                Return Nothing
            End If
        End Function

        Public Sub AddValue(value As DebugValue)
            SyncLock (_Values)
                If value.IsField Then
                    SyncLock (FieldLookup)
                        FieldLookup.Add(value.FieldReference, _Values.Length)
                    End SyncLock
                ElseIf value.IsProperty Then
                    SyncLock (PropertyLookup)
                        PropertyLookup.Add(value.PropertyReference, _Values.Length)
                    End SyncLock
                End If
                _Values.Add(value)
            End SyncLock
        End Sub

        Public Sub SetField(f As FieldReference, value As DebugValue)
            SyncLock (FieldLookup)
                If Not FieldLookup.ContainsKey(f) Then Return
                SyncLock (_Values)
                    _Values(FieldLookup(f)) = value
                End SyncLock
            End SyncLock
        End Sub

        Public Sub SetProperty(p As PropertyReference, value As DebugValue)
            SyncLock (PropertyLookup)
                If Not PropertyLookup.ContainsKey(p) Then Return
                SyncLock (_Values)
                    _Values(PropertyLookup(p)) = value
                End SyncLock
            End SyncLock
        End Sub

        Public Function Clone() As DebugValueCollection
            Dim c As DebugValueCollection = CloneObject(Of DebugValueCollection)
            Return c
        End Function

        Public Sub New() : End Sub
        Public Sub New(ValueChanged As Boolean, Values As List(Of DebugValue))
            _ValueChanged = ValueChanged
            _Values = Values
        End Sub
    End Class

End Namespace