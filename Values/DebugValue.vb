Imports System.Threading
Imports System.Windows
Imports JackDebug.WPF.Async
Imports JackDebug.WPF.Collections
Imports MicroSerializationLibrary.Serialization

Namespace Values
    Public Class DebugValue
        Implements IDisposable

#Region "Properties"
        Private Property ChildrenNotIndexed As Boolean = True

        Public Property GUID As String
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

        Public Property Children As DebugValueCollection
            Get
                Return _Children
            End Get
            Set(value As DebugValueCollection)
                _Children = value
            End Set
        End Property
        Private _Children As New DebugValueCollection

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

        Public Property Timecode As Long
            Get
                Return _Timecode
            End Get
            Set(value As Long)
                _Timecode = value
            End Set
        End Property
        Private _Timecode As Long
#End Region

#Region "Caches"
        Private LastFields As New Dictionary(Of FieldReference, DebugValue)
        Private LastProperties As New Dictionary(Of PropertyReference, DebugValue)

        Private ChildFields As New List(Of FieldReference)
        Private ChildProperties As New List(Of PropertyReference)
#End Region

#Region "Shared"

        ''' <summary>
        ''' Maximum time allowed for a type to be reflected and output before it get's blacklisted and no longer reported.
        ''' </summary>
        ''' <returns>Default is 0.125 Seconds.</returns>
        Public Shared Property AutoIgnoreSlowTypesMaxTime As Double = 0.125
        Public Shared Property AutoIgnoreSlowTypes As Boolean = True
        Public Shared Property IgnoreTypes As Type() = New Type() {GetType(Bitmap)}
        Public Shared Function NewFieldValue(FieldReference As FieldReference, Instance As Object, isRecursive As Boolean) As DebugValue
            Dim Start As DateTime = DateTime.Now
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
            Dim TimeToCalculate As TimeSpan = DateTime.Now - Start
            If AutoIgnoreSlowTypes AndAlso TimeToCalculate.TotalSeconds > AutoIgnoreSlowTypesMaxTime Then
                IgnoreTypes.Add(t)
            End If
            Return New DebugValue(FieldReference.Info.Name, FieldReference, Nothing, t, Flags, CurrentValue, True, Nothing, ChangedIndexies, isRecursive) With {._CalculationTime = TimeToCalculate}
        End Function

        Public Shared Function NewPropertyValue(PropertyReference As PropertyReference, Instance As Object, isRecursive As Boolean) As DebugValue
            Dim Start As DateTime = DateTime.Now
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
            Dim TimeToCalculate As TimeSpan = DateTime.Now - Start
            If AutoIgnoreSlowTypes AndAlso TimeToCalculate.TotalSeconds > AutoIgnoreSlowTypesMaxTime Then
                IgnoreTypes.Add(t)
            End If
            Return New DebugValue(PropertyReference.Info.Name, Nothing, PropertyReference, t, Flags, CurrentValue, True, Nothing, ChangedIndexies, isRecursive) With {._CalculationTime = TimeToCalculate}
        End Function

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

        Public Function SetSubValues(SubValues As DebugValueCollection) As DebugValue
            _Children = SubValues
            Return Me
        End Function

        Public Function SetFlags(Flags As TypeFlags) As DebugValue
            _Flags = Flags
            Return Me
        End Function

        Public Shared Function GetTypeFlags(SafeValue As SafeValue, t As Type) As TypeFlags
            Dim Flags As New TypeFlags With {
            .isSystem = t.Namespace.StartsWith("System"),
            .isBoolean = t Is GetType(Boolean),
            .isIgnored = IgnoreTypes.Contains(t),
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
            End If

            Flags.isNumeric = IsNumeric(SafeValue.Value) Or Flags.isBoolean
            Return Flags
        End Function

#End Region

#Region "Value Functions"
        Public Function GetPropertyValue(PropertyReference As PropertyReference, LastValue As DebugValue, Instance As Object, ParentInstances As Object(), isRecursive As Boolean) As DebugValueUpdate

            Me.IsRecursive = isRecursive
            Dim SafeValue As New SafeValue(ResolveValue(PropertyReference, Instance))
            Dim CurrentValue As Object = SafeValue.Value
            Dim t As Type = PropertyReference.Info.PropertyType
            Dim Flags As TypeFlags = GetTypeFlags(SafeValue, t)
            Dim ValueChanged As Boolean = False
            Dim isIgnored As Boolean = IgnoreTypes.Contains(t)
            Dim ChangedIndexies As List(Of Integer)

            If Flags.isArray Then
                Dim Results As ArrayCompareResults = CompareArray(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isList Then
                Dim Results As ArrayCompareResults = CompareList(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isDictionary Then
                Dim Results As ArrayCompareResults = CompareDictionary(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Not isIgnored Then
                ValueChanged = DidValueChange(CurrentValue, If(LastValue IsNot Nothing, LastValue.Value, Nothing))
            End If

            Return New DebugValueUpdate(LastValue, CurrentValue, ValueChanged)
        End Function

        Public Function CompareChildValue(CurrentValue As DebugValue, LastValue As DebugValue, Instance As Object) As DebugValueUpdate
            Dim ChangedIndexies As New List(Of Integer)
            If Flags.isArray Then
                Dim Results As ArrayCompareResults = CompareArray(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isList Then
                Dim Results As ArrayCompareResults = CompareList(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Flags.isDictionary Then
                Dim Results As ArrayCompareResults = CompareDictionary(CurrentValue, LastValue)
                ChangedIndexies = Results.ChangedIndexies
                ValueChanged = Results.ValueChanged
            ElseIf Not Flags.isIgnored Then
                ValueChanged = DidValueChange(CurrentValue, LastValue)
            End If

            Return New DebugValueUpdate(LastValue, CurrentValue, ValueChanged)
        End Function

        Private Function GetAllChildDebugValueCollection(Instance As Object, Parents As Object()) As DebugValueCollection
            Dim out As New DebugValueCollection
            For i As Integer = 0 To ChildFields.Count - 1
                Dim Start As DateTime = DateTime.Now
                Dim ChildField As FieldReference = ChildFields(i)
                If DebugValue.IgnoreTypes.Contains(ChildField.Info.FieldType) Then Continue For

                Dim ChildValue As DebugValue = NewFieldValue(ChildField, Instance, IsRecursive)

                If ChildValue.Value IsNot Nothing Then
                    If Not Parents.Contains(ChildValue.Value) Then
                        Parents = Parents.AddJoin(ChildValue.Value)
                    End If

                    If NotNothing(Parents) AndAlso Parents.Contains(ChildValue) Then
                        Continue For
                    End If

                    If Not Children.ContainsField(ChildField) Then
                        Children.AddValue(ChildValue)
                        out.AddValue(ChildValue.SetValueChanged(True))
                    Else
                        Dim LastChildValue As DebugValue = Children.GetField(ChildField)
                        Dim CompareResult As DebugValueUpdate = CompareChildValue(ChildValue, LastChildValue, Instance)
                        Children.SetField(ChildField, ChildValue)
                        If CompareResult.ValueChanged Then out.SetChanged()
                        out.AddValue(CompareResult.CurrentValue)
                    End If
                End If
                Dim TimeToCalculate As TimeSpan = DateTime.Now - Start
                If AutoIgnoreSlowTypes AndAlso TimeToCalculate.TotalSeconds > AutoIgnoreSlowTypesMaxTime Then
                    If AutoIgnoreSlowTypes Then
                        If Not IgnoreTypes.Contains(ChildField.Info.FieldType) Then
                            IgnoreTypes.Add(ChildField.Info.FieldType)
                        End If
                    End If
                End If
            Next

            For i As Integer = 0 To ChildProperties.Count - 1
                Dim Start As DateTime = DateTime.Now
                Dim ChildProperty As PropertyReference = ChildProperties(i)
                If IgnoreTypes.Contains(ChildProperty.Info.PropertyType) Then Continue For

                Dim ChildValue As DebugValue = NewPropertyValue(ChildProperty, Instance, IsRecursive)

                If ChildValue.Value IsNot Nothing Then
                    If Not Parents.Contains(ChildValue.Value) Then
                        Parents = Parents.AddJoin(ChildValue.Value)
                    End If

                    If NotNothing(Parents) AndAlso Parents.Contains(ChildValue) Then
                        Continue For
                    End If

                    If Not Children.ContainsProperty(ChildProperty) Then
                        Children.AddValue(ChildValue)
                        out.AddValue(ChildValue.SetValueChanged(True))
                    Else
                        Dim LastChildValue As DebugValue = Children.GetProperty(ChildProperty)
                        Dim CompareResult As DebugValueUpdate = CompareChildValue(ChildValue, LastChildValue, Instance)
                        Children.SetProperty(ChildProperty, ChildValue)
                        If CompareResult.ValueChanged Then out.SetChanged()
                        out.AddValue(CompareResult.CurrentValue)
                    End If
                End If
                Dim TimeToCalculate As TimeSpan = DateTime.Now - Start
                If AutoIgnoreSlowTypes AndAlso TimeToCalculate.TotalSeconds > AutoIgnoreSlowTypesMaxTime Then
                    If Not IgnoreTypes.Contains(ChildProperty.Info.PropertyType) Then
                        IgnoreTypes.Add(ChildProperty.Info.PropertyType)
                    End If
                End If
            Next

            Flags.CalculatingChildren = False
            Return out
        End Function

        Public Async Function UpdateValue(WorkerState As ChildValueWorkerState) As Task(Of Boolean)
            Dim SafeValue As New SafeValue(Nothing)

            If IsField Then
                SafeValue = New SafeValue(DirectCast(WorkerState.Reference, FieldReference).Info.GetValue(WorkerState.Instance))
                _FieldReference = WorkerState.Reference
                _IsField = True
            ElseIf IsProperty Then
                SafeValue = New SafeValue(DirectCast(WorkerState.Reference, PropertyReference).Info.GetValue(WorkerState.Instance))
                _PropertyReference = WorkerState.Reference
                _IsProperty = True
            End If

            Dim CurrentValue = SafeValue.Value

            Dim ValueChanged As Boolean = False

            If Not SafeValue.IsNothing Then
                _Type = CurrentValue.GetType()
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

                SetValueChanged(ValueChanged)

                If Not Flags.isSystem AndAlso ChildrenNotIndexed Then
                    UpdateChildReferences(CurrentValue)
                ElseIf Not Flags.isSystem AndAlso Not Flags.CalculatingChildren Then
                    WorkerState.Instance = CurrentValue
                    Flags.CalculatingChildren = True
                    '''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
                    Children = GetAllChildDebugValueCollection(WorkerState.Instance, WorkerState.Parents)
                    '''''''''''''''''''''''''''''''''''''''''''''''''''''''''''''
                    Flags.CalculatingChildren = False
                    SetValueChanged(Children.ValueChanged)
                End If
            End If

            _LastValue = Value
            _Value = CurrentValue
            Return True
        End Function

        Public Function Clone() As DebugValue
            Dim c As DebugValue = CloneObject(Of DebugValue)
            If Not c.Flags.isSystem AndAlso c.ChildrenNotIndexed Then c.UpdateChildReferences(c.Value)
            Return c
        End Function

        Private Sub UpdateChildReferences(CurrentValue As Object)
            If NotNothing(CurrentValue) Then
                ChildrenNotIndexed = False
                ChildProperties = DeserializationWrapper.GetPropertyReferences(Value)
                ChildFields = DeserializationWrapper.GetFieldReferences(Value)
            End If
        End Sub

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
                    If NotNothing(_Children) Then _Children.Dispose()
                    _Children = Nothing
                    _Timecode = Nothing
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
            _Children = Children
            _Timecode = DateTime.UtcNow().Ticks
            _IsRecursive = IsRecursive
            If Not Flags.isSystem AndAlso ChildrenNotIndexed Then UpdateChildReferences(Value)
        End Sub

    End Class


End Namespace