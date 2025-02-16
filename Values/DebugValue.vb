Imports System.ComponentModel
Imports System.Threading
Imports System.Windows
Imports JackDebug.WPF.Collections
Imports JackDebug.WPF.States
Imports MicroSerializationLibrary.Serialization
Imports Microsoft.Xaml.Behaviors

Namespace Values
    Public Class DebugValue
        Implements IDisposable

#Region "Properties"
        Public Property Guid As String
            Get
                Return _GUID
            End Get
            Set(value As String)
                _GUID = value
            End Set
        End Property
        Private _GUID As String

        Public Property Name As String
            Get
                Return _Name
            End Get
            Set(value As String)
                _Name = value
            End Set
        End Property
        Private _Name As String

        Public Property Type As Type
            Get
                Return _Type
            End Get
            Set(value As Type)
                _Type = value
            End Set
        End Property
        Private _Type As Type

        Public Property Value As Object
            Get
                Return _Value
            End Get
            Set(value As Object)
                _Value = value
            End Set
        End Property
        Private _Value As Object

        Public Property LastValue As Object
            Get
                Return _LastValue
            End Get
            Set(value As Object)
                _LastValue = value
            End Set
        End Property
        Private _LastValue As Object

        Public Property ChangedIndexies As List(Of Integer)
            Get
                Return _ChangedIndexies
            End Get
            Set(value As List(Of Integer))
                _ChangedIndexies = value
            End Set
        End Property
        Private _ChangedIndexies As New List(Of Integer)

        Public Property KeyList As IList
            Get
                Return _KeyList
            End Get
            Set(value As IList)
                _KeyList = value
            End Set
        End Property
        Private _KeyList As IList

        Public Property ValueList As IList
            Get
                Return _ValueList
            End Get
            Set(value As IList)
                _ValueList = value
            End Set
        End Property
        Private _ValueList As IList

        Public Property FieldReference As FieldReference
            Get
                Return _FieldReference
            End Get
            Set(value As FieldReference)
                _FieldReference = value
            End Set
        End Property
        Private _FieldReference As FieldReference

        Public Property PropertyReference As PropertyReference
            Get
                Return _PropertyReference
            End Get
            Set(value As PropertyReference)
                _PropertyReference = value
            End Set
        End Property
        Private _PropertyReference As PropertyReference
        Public Property ValueChanged As Boolean
            Get
                Return _ValueChanged
            End Get
            Set(value As Boolean)
                _ValueChanged = value
            End Set
        End Property
        Private _ValueChanged As Boolean

        Public Property CalculationTime As TimeSpan
            Get
                Return _CalculationTime
            End Get
            Set(value As TimeSpan)
                _CalculationTime = value
            End Set
        End Property
        Private _CalculationTime As TimeSpan

        Public Property IsField As Boolean
            Get
                Return _IsField
            End Get
            Set(value As Boolean)
                _IsField = value
            End Set
        End Property
        Private _IsField As Boolean = False

        Public Property IsProperty As Boolean
            Get
                Return _IsProperty
            End Get
            Set(value As Boolean)
                _IsProperty = value
            End Set
        End Property
        Private _IsProperty As Boolean = False

        Public Property Flags As TypeFlags
            Get
                Return _Flags
            End Get
            Set(value As TypeFlags)
                _Flags = value
            End Set
        End Property
        Private _Flags As New TypeFlags()

        Public Property IsRecursive As Boolean
            Get
                Return _IsRecursive
            End Get
            Set(value As Boolean)
                _IsRecursive = value
            End Set
        End Property
        Private _IsRecursive As Boolean

        Public ReadOnly Property Length As Integer
            Get
                If IsNothing(Value) Then Return -1
                If Flags.isArray Then
                    Return Value.Length
                ElseIf Flags.isDictionary Or Flags.isList Then
                    Return Value.Count
                Else
                    Return -1
                End If
            End Get
        End Property

        Public Property Timecode As Integer
            Get
                Return _Timecode
            End Get
            Set(value As Integer)
                _Timecode = value
            End Set
        End Property
        Private _Timecode As Integer

#End Region

#Region "Overrides"

        Public Overrides Function ToString() As String
            Return Name & " (" & Type.Name & "): " & If(IsNothing(Value), "NULL", Value.ToString())
        End Function

#End Region

#Region "Shared"

        Public Shared Function NewArrayOrListValue(Watcher As DebugWatcher, Index As Integer, Type As Type, Instance As Object, isRecursive As Boolean) As DebugValue
            Dim Start As DateTime = DateTime.UtcNow
            Dim SafeValue As New SafeValue(Instance(Index))
            Dim CurrentValue As Object = SafeValue.Value

            Dim Flags As TypeFlags = GetCollectionItemTypeFlags(SafeValue, Type, Index)

            Dim TimeToCalculate As TimeSpan = DateTime.UtcNow - Start

            Dim Out As New DebugValue("[" & Index & "]" & Type.Name, Nothing, Nothing, Type, Flags, CurrentValue, True, Nothing, New List(Of Integer), isRecursive) With {._CalculationTime = TimeToCalculate}
            Out.Guid = Watcher.ParentValueGuid

            TryCreateChildWatcher(Watcher, Flags, CurrentValue, isRecursive, Out, Index)
            Return Out
        End Function

        Public Shared Function NewFieldValue(Watcher As DebugWatcher, FieldReference As FieldReference, Instance As Object, isRecursive As Boolean) As DebugValue
            Dim Start As DateTime = DateTime.UtcNow
            Dim SafeValue As New SafeValue(FieldReference.Info.GetValue(Instance))
            Dim CurrentValue As Object = SafeValue.Value

            Dim t As Type = FieldReference.Info.FieldType
            Dim Flags As TypeFlags = GetTypeFlags(SafeValue, t)

            Dim ChangedIndexies As New List(Of Integer)
            If Flags.isArray Then
                Dim Results As ArrayCompareResults = CompareArray(CurrentValue, Nothing)
                ChangedIndexies = Results.ChangedIndexies
            ElseIf Flags.isList Then
                Dim Results As ArrayCompareResults = CompareList(CurrentValue, Nothing)
                ChangedIndexies = Results.ChangedIndexies
            ElseIf Flags.isDictionary Then
                Dim Results As ArrayCompareResults = CompareDictionary(CurrentValue, Nothing)
                ChangedIndexies = Results.ChangedIndexies
            End If
            Dim TimeToCalculate As TimeSpan = DateTime.UtcNow - Start
            If Watcher.AutoIgnoreSlowTypes AndAlso TimeToCalculate.TotalSeconds > Watcher.AutoIgnoreSlowTypesMaxTime Then
                DebugWatcher.IgnoreTypes.Add(t)
            End If

            Dim Out As New DebugValue(FieldReference.Info.Name, FieldReference, Nothing, t, Flags, CurrentValue, True, Nothing, ChangedIndexies, isRecursive) With {._CalculationTime = TimeToCalculate}
            TryCreateChildWatcher(Watcher, Flags, CurrentValue, isRecursive, Out, FieldReference.Index)
            Return Out
        End Function

        Public Shared Function NewPropertyValue(Watcher As DebugWatcher, PropertyReference As PropertyReference, Instance As Object, isRecursive As Boolean) As DebugValue
            Dim Start As DateTime = DateTime.UtcNow
            Dim SafeValue As New SafeValue(PropertyReference.Info.GetValue(Instance))
            Dim CurrentValue As Object = SafeValue.Value

            Dim t As Type = PropertyReference.Info.PropertyType
            Dim Flags As TypeFlags = GetTypeFlags(SafeValue, t)

            Dim ChangedIndexies As New List(Of Integer)
            If Flags.isArray Then
                Dim Results As ArrayCompareResults = CompareArray(CurrentValue, Nothing)
                ChangedIndexies = Results.ChangedIndexies
            ElseIf Flags.isList Then
                Dim Results As ArrayCompareResults = CompareList(CurrentValue, Nothing)
                ChangedIndexies = Results.ChangedIndexies
            ElseIf Flags.isDictionary Then
                Dim Results As ArrayCompareResults = CompareDictionary(CurrentValue, Nothing)
                ChangedIndexies = Results.ChangedIndexies
            End If
            Dim TimeToCalculate As TimeSpan = DateTime.UtcNow - Start
            If Watcher.AutoIgnoreSlowTypes AndAlso TimeToCalculate.TotalSeconds > Watcher.AutoIgnoreSlowTypesMaxTime Then
                DebugWatcher.IgnoreTypes.Add(t)
            End If
            Dim Out As New DebugValue(PropertyReference.Info.Name, Nothing, PropertyReference, t, Flags, CurrentValue, True, Nothing, ChangedIndexies, isRecursive) With {._CalculationTime = TimeToCalculate}
            TryCreateChildWatcher(Watcher, Flags, CurrentValue, isRecursive, Out, PropertyReference.Index)
            Return Out
        End Function

        Public Sub TryCreateChildWatcher(Watcher As DebugWatcher, CurrentValue As Object, Index As Integer)
            TryCreateChildWatcher(Watcher, Flags, CurrentValue, IsRecursive, Me, Index)
        End Sub

        Public Shared Sub TryCreateChildWatcher(Watcher As DebugWatcher, Flags As TypeFlags, CurrentValue As Object, IsRecursive As Boolean, Out As DebugValue, Index As Integer)
            If Not Flags.isSystem AndAlso Not Flags.isNothing AndAlso IsRecursive AndAlso Not Watcher.ChildIndexies.Contains(Index) Then
                ''' Recursive Watcher creation.
                Dim isDictionary As Boolean = Utils.IsDictionary(CurrentValue)
                Dim isArray As Boolean = Utils.isArray(CurrentValue)
                Dim isList As Boolean = Utils.IsList(CurrentValue)
                If isArray Or isList Then
                    DebugWatcher.CreateChild(Watcher, Out, CurrentValue, IsRecursive, True, Index)
                ElseIf Not isDictionary Then
                    DebugWatcher.CreateChild(Watcher, Out, CurrentValue, IsRecursive, False, Index)
                End If
                Flags.IsWatched = True
            End If
        End Sub

#End Region

#Region "Events"

        Public Event OnValueChanged(OldVal, NewVal)

        Public Sub InvokeValueChanged(OldVal, NewVal)
            _ValueChanged = True
            RaiseEvent OnValueChanged(OldVal, NewVal)
        End Sub

#End Region

#Region "Public Functions"

        Public Function SetValueChanged(ValueChanged As Boolean) As DebugValue
            _ValueChanged = ValueChanged
            Return Me
        End Function

        Public Function SetChangedIndexies(ChangedIndexies As List(Of Integer)) As DebugValue
            _ChangedIndexies = ChangedIndexies
            Return Me
        End Function

        Public Function SetFlags(Flags As TypeFlags) As DebugValue
            _Flags = Flags
            Return Me
        End Function

        Public Shared Function GetCollectionItemTypeFlags(SafeValue As SafeValue, t As Type, Index As Integer) As TypeFlags
            Dim Flags As TypeFlags = GetTypeFlags(SafeValue, t)
            Flags.isCollectionItem = True
            Flags.CollectionIndex = Index
            Return Flags
        End Function

        Public Shared Function GetTypeFlags(SafeValue As SafeValue, t As Type) As TypeFlags
            Dim Flags As New TypeFlags With {
            .isSystem = IsSystemType(t),
            .isBoolean = t Is GetType(Boolean),
            .isIgnored = DebugWatcher.IgnoreTypes.Contains(t),
            .isXAML = t Is GetType(DependencyObject)
        }

            If SafeValue.IsNothing Then
                Flags.isNothing = SafeValue.IsNothing
            Else
                With Flags
                    .isArray = isArray(SafeValue.Value)
                    .isList = IsList(SafeValue.Value)
                    .isDictionary = IsDictionary(SafeValue.Value)
                End With
                Flags.isCollection = Flags.isArray Or Flags.isList Or Flags.isDictionary
            End If

            Flags.isNumeric = IsNumeric(SafeValue.Value) Or Flags.isBoolean
            Return Flags
        End Function

#End Region

#Region "Value Functions"

        Public Function UpdateValue(WorkerState As ValueWorkerState) As DebugValue
            Dim SafeValue As New SafeValue(Nothing)

            If IsField AndAlso Flags.isSystem Then
                SafeValue = New SafeValue(DirectCast(WorkerState.Reference, FieldReference).Info.GetValue(WorkerState.Instance))
                _FieldReference = WorkerState.Reference
                _IsField = True
            ElseIf IsProperty AndAlso Flags.isSystem Then
                SafeValue = New SafeValue(DirectCast(WorkerState.Reference, PropertyReference).Info.GetValue(WorkerState.Instance))
                _PropertyReference = WorkerState.Reference
                _IsProperty = True
            ElseIf IsField AndAlso Not Flags.isSystem Then
                SafeValue = New SafeValue(DirectCast(WorkerState.Reference, FieldReference).Info.FieldType.Name & "*")
                _FieldReference = WorkerState.Reference
                _IsField = True
            ElseIf IsProperty AndAlso Not Flags.isSystem Then
                SafeValue = New SafeValue(DirectCast(WorkerState.Reference, PropertyReference).Info.PropertyType.Name & "*")
                _PropertyReference = WorkerState.Reference
                _IsProperty = True
            End If

            Dim CurrentValue = SafeValue.Value
            Dim ValueChanged As Boolean = False

            _Flags = GetTypeFlags(SafeValue, Type)
            If Flags.isArray Then
                Dim Results As ArrayCompareResults = CompareArray(CurrentValue, LastValue)
                _ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isList Then
                Dim Results As ArrayCompareResults = CompareList(CurrentValue, LastValue)
                _ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isDictionary Then
                Dim Results As ArrayCompareResults = CompareDictionary(CurrentValue, LastValue)
                _ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isNumeric Then
                ValueChanged = CurrentValue <> LastValue
            ElseIf Not Flags.isIgnored Then
                ValueChanged = DidValueChange(Value, LastValue)
            End If

            _LastValue = Value
            _Value = CurrentValue
            TryCreateChildWatcher(WorkerState.Watcher, CurrentValue, WorkerState.Reference.Index)
            Return SetValueChanged(ValueChanged)
        End Function

        Public Function Clone() As DebugValue
            Dim c As DebugValue = CloneObject(Of DebugValue)
            Return c
        End Function


#End Region

#Region "IDisposable"
        Private disposedValue As Boolean
        Protected Overridable Sub Dispose(disposing As Boolean)
            If Not disposedValue Then
                If disposing Then
                    _GUID = Nothing
                    _Name = Nothing
                    _Type = Nothing
                    _Value = Nothing
                    _Flags = Nothing
                    _LastValue = Nothing
                    _ValueChanged = Nothing
                    _IsField = Nothing
                    _IsProperty = Nothing
                    _FieldReference = Nothing
                    _PropertyReference = Nothing
                    _ChangedIndexies = Nothing
                    _Timecode = Nothing
                    _CalculationTime = Nothing
                End If

                disposedValue = True
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose

            Dispose(disposing:=True)
            GC.SuppressFinalize(Me)
        End Sub
#End Region

        Public Sub New() : End Sub
        Public Sub New(Name As String, FieldReference As FieldReference, PropertyReference As PropertyReference, Type As Type, Flags As TypeFlags, Value As Object,
                     Optional ValueChanged As Boolean = False,
                     Optional LastValue As Object = Nothing,
                     Optional ChangedIndexies As List(Of Integer) = Nothing, Optional IsRecursive As Boolean = False)

            Dim SafeValue As New SafeValue(Value)
            _GUID = System.Guid.NewGuid().ToString()
            _Name = Name
            _Type = Type
            _Value = SafeValue.Value
            _Flags = Flags
            _LastValue = LastValue
            _ValueChanged = ValueChanged
            _IsField = NotNothing(FieldReference)
            _IsProperty = NotNothing(PropertyReference)
            _FieldReference = FieldReference
            _PropertyReference = PropertyReference
            _ChangedIndexies = ChangedIndexies
            If DebugWatcher.FirstTimeEnabled = Nothing Then DebugWatcher.FirstTimeEnabled = DateTime.UtcNow
            _Timecode = DateTime.UtcNow.Ticks - DebugWatcher.FirstTimeEnabled.Ticks
            _IsRecursive = IsRecursive
        End Sub

    End Class


End Namespace