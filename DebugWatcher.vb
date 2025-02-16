Imports System.ComponentModel
Imports MicroSerializationLibrary.Serialization
Imports MicroSerializationLibrary
Imports System.Runtime.CompilerServices
Imports System.Windows.Threading
Imports System.Windows
Imports System.Reflection
Imports System.Threading
Imports System.Windows.Input.Manipulations
Imports System.Text.RegularExpressions
Imports System.Runtime.InteropServices
Imports JackDebug.WPF.Values
Imports JackDebug.WPF.Collections
Imports MicroSerializationLibrary.Networking
Imports JackDebug.WPF.DebugWatcher
Imports JackDebug.WPF.States
Imports System.Windows.Documents

Public Class DebugWatcher
    Implements IDisposable

#Region "IDisposable"
    Private disposedValue As Boolean
    Protected Overridable Sub Dispose(disposing As Boolean)
        If Not disposedValue Then
            If disposing Then
                DisposeWorkers()
            End If

            disposedValue = True
        End If
    End Sub
    Public Sub Dispose() Implements IDisposable.Dispose
        Dispose(disposing:=True)
        GC.SuppressFinalize(Me)
    End Sub
#End Region

#Region "Caches"

    Private CollectionValues As New SortedDictionary(Of Integer, DebugValue)
    Private FieldValues As New SortedDictionary(Of Integer, DebugValue)
    Private PropertyValues As New SortedDictionary(Of Integer, DebugValue)

    Public ChildWatcherValues As New List(Of String)
    Public ChildWatcherGuids As New List(Of String)
    Public ChildIndexies As New List(Of Integer)

#End Region

#Region "Shared"

    ''' <summary>
    ''' Maximum time allowed for a type to be reflected and output before it get's blacklisted and no longer reported.
    ''' </summary>
    ''' <returns>Default is 0.125 Seconds.</returns>
    Public Property AutoIgnoreSlowTypesMaxTime As Double = 0.125
    Public Property AutoIgnoreSlowTypes As Boolean = True
    Public Shared Property IgnoreTypes As Type() = New Type() {GetType(Bitmap)}

    Public Shared Property EnableDispatcherProperties As Boolean = False
    Public Shared Property BlacklistedTypes As New List(Of Type)
    Public Shared Property Watchers As New Dictionary(Of String, DebugWatcher)
    Public Shared Property DebugWindow As DebugWindow

    Public Shared Sub CreateDebugWindow()
        If DebugWindow IsNot Nothing Then
            If DebugWindow.Enabled Then
                DebugWindow.Enabled = False
                DebugWindow.Close()
                DebugWindow = Nothing
            End If
        End If
        DebugWindow = New DebugWindow()
        DebugWindow.Show()
    End Sub

#End Region

#Region "Events"

    Public Event EnabledStateChanged(isEnabled As Boolean)

    ''' <summary>
    ''' Raised when the watcher has a new value.
    ''' </summary>
    ''' <param name="Watcher">The associated Watcher</param>
    ''' <param name="Value">The Value that was processed</param>
    Public Event ValueCalculated(Watcher As DebugWatcher, Value As DebugValue)

    ''' <summary>
    ''' Raised when non-system collection value calculation is finished.
    ''' </summary>
    ''' <param name="Watcher">The associated Watcher</param>
    ''' <param name="Value">The Value that was processed</param>
    Public Event CollectionCalculated(Watcher As DebugWatcher, Values As List(Of DebugValue), ParentValueGuid As String)

    ''' <summary>
    ''' Raised when main & child Item Value Changes.
    ''' </summary>
    ''' <param name="Watcher">The associated Watcher</param>
    ''' <param name="Value">The Value that changed</param>
    ''' <param name="ArrayIndexies">The Index(s) that have changed</param>
    Public Event ValueChanged(Watcher As DebugWatcher, Value As DebugValue, ArrayIndexies As List(Of Integer))

    Public Event ArrayValueChanged(Watcher As DebugWatcher, Value As DebugValue, Index As Integer)

    Public Sub OnChangedValue(Watcher As DebugWatcher, ChangedValue As DebugValue)
        RaiseEvent ValueChanged(Watcher, ChangedValue, ChangedValue.ChangedIndexies)
    End Sub

    Public Sub OnValueCalculated(Watcher As DebugWatcher, Value As DebugValue)
        RaiseEvent ValueCalculated(Watcher, Value)
    End Sub

#End Region

#Region "Properties"

    Public Property Guid As String = System.Guid.NewGuid().ToString()

    Public Property ValueCount As Integer = 0

    Public Shared Property FirstTimeEnabled As DateTime = Nothing

    Public ReadOnly Property Name As String
        Get
            Return _Name
        End Get
    End Property
    Private _Name As String

    Public ReadOnly Property AttachedObject
        Get
            Return _AttachedObject
        End Get
    End Property
    Private _AttachedObject As Object

    Public Property IsRecursive As Boolean
        Get
            Return _Recursive
        End Get
        Set(value As Boolean)
            _Recursive = value
        End Set
    End Property
    Private _Recursive As Boolean = False

    Public Property Parent As DebugWatcher
        Get
            Return _Parent
        End Get
        Set(value As DebugWatcher)
            _Parent = value
        End Set
    End Property
    Private _Parent As DebugWatcher

    Public ReadOnly Property ParentValueGuid As String
        Get
            Return _ParentValueGuid
        End Get
    End Property
    Private _ParentValueGuid As String

    Public ReadOnly Property ValueType As Type
        Get
            Return _ValueType
        End Get
    End Property
    Private _ValueType As Type

    Public ReadOnly Property CollectionType As Type
        Get
            Return _CollectionType
        End Get
    End Property
    Private _CollectionType As Type

    Public ReadOnly Property IsCollectionOf As Boolean
        Get
            Return _IsCollectionOf
        End Get
    End Property
    Private _IsCollectionOf As Boolean = False

    Public ReadOnly Property CollectionIndex As Integer
        Get
            Return _CollectionIndex
        End Get
    End Property
    Private _CollectionIndex As Integer = -1

    Public ReadOnly Property IsChildWatcher As Boolean
        Get
            Return _IsChildWatcher
        End Get
    End Property
    Private _IsChildWatcher As Boolean = False

    Public Property isEnabled As Boolean
        Get
            Return _isEnabled
        End Get
        Set(value As Boolean)
            Dim oldState As Boolean = isEnabled
            _isEnabled = value
            If isEnabled Then CreateWorkers()
            For i As Integer = 0 To ChildWatcherGuids.Count - 1
                Dim ChildGuid As String = ChildWatcherGuids(i)
                If DebugWatcher.Watchers.ContainsKey(ChildGuid) Then
                    Dim w As DebugWatcher = DebugWatcher.Watchers(ChildGuid)
                    w.isEnabled = isEnabled
                End If
            Next
            If oldState <> isEnabled Then RaiseEvent EnabledStateChanged(isEnabled)
        End Set
    End Property
    Private _isEnabled As Boolean = False

    Public ReadOnly Property Fields As FieldReference()
        Get
            Return _Fields
        End Get
    End Property
    Private _Fields As FieldReference()

    Public ReadOnly Property Properties As PropertyReference()
        Get
            Return _Properties
        End Get
    End Property
    Private _Properties As PropertyReference()

    Public ReadOnly Property isProperty As Boolean
        Get
            Return _isProperty
        End Get
    End Property
    Private _isProperty As Boolean = False

#End Region

#Region "Initializers"

    ''' <summary>
    ''' Create a new DebugWatcher
    ''' </summary>
    Public Sub New(AttachedTo As Object)
        Initialize(AttachedTo)
    End Sub

    ''' <summary>
    ''' Create a new DebugWatcher
    ''' </summary>
    ''' <param name="AttachedTo">The target object for debugging.</param>
    ''' <param name="Recursive">Recursive functionality disabled until finished.</param>
    Public Sub New(AttachedTo As Object, Recursive As Boolean)
        _Recursive = Recursive
        Initialize(AttachedTo)
    End Sub

    ''' <summary>
    ''' Only for use internally.
    ''' </summary>
    ''' <param name="Parent"></param>
    ''' <param name="ParentValueGuid"></param>
    ''' <param name="AttachedTo"></param>
    ''' <param name="Recursive">Recursive functionality disabled until finished.</param>
    ''' <param name="IsCollectionOf">This is for if the value is an array/list. Dictionary not yet supported.</param>
    Public Sub New(Parent As DebugWatcher, ParentValueGuid As String, AttachedTo As Object, Recursive As Boolean, Optional CollectionIndex As Integer = -1, Optional IsCollectionOf As Boolean = False, Optional IsChild As Boolean = False)
        _Recursive = Recursive
        _Parent = Parent
        _IsCollectionOf = IsCollectionOf
        _ParentValueGuid = ParentValueGuid
        _IsChildWatcher = IsChild
        _CollectionIndex = CollectionIndex
        ChildIndexies.Add(CollectionIndex)
        Parent.ChildWatcherValues.Add(ParentValueGuid)
        Parent.ChildWatcherGuids.Add(Guid)
        Initialize(AttachedTo, "", IsCollectionOf)
        isEnabled = Parent.isEnabled
    End Sub

    Private Sub Initialize(ByRef AttachedTo As Object, Optional Name As String = "", Optional isCollectionOf As Boolean = False)
        _Name = If(Name = "", AttachedTo.GetType().ToString(), Name)
        _AttachedObject = AttachedTo
        DeserializationWrapper.ReflectionFlags = BindingFlags.Instance Or BindingFlags.[Public] Or BindingFlags.NonPublic

        _ValueType = AttachedObject.GetType()

        Dim isArray As Boolean = Utils.isArray(AttachedTo)
        Dim isList As Boolean = Utils.IsList(AttachedTo)

        If isArray Then
            _CollectionType = ValueType.GetElementType()
        ElseIf isList Then
            _CollectionType = ValueType.GenericTypeArguments.First()
        End If

        If isCollectionOf AndAlso Not Utils.IsSystemType(CollectionType) Then
            _Fields = DeserializationWrapper.GetFieldReferences(CollectionType).ToArray()
            _Properties = DeserializationWrapper.GetPropertyReferences(CollectionType).ToArray()
        Else
            _Fields = DeserializationWrapper.GetFieldReferences(ValueType).ToArray()
            _Properties = DeserializationWrapper.GetPropertyReferences(ValueType).ToArray()
        End If

        ValueCount = If(Fields IsNot Nothing, _Fields.Count, 0) + If(Properties IsNot Nothing, _Properties.Count, 0)
        If NotNothing(Parent) Then
            isEnabled = Parent.isEnabled
        End If
        DebugWatcher.Watchers.Add(Guid, Me)
    End Sub

    Public Shared Function CreateChild(Parent As DebugWatcher, ParentValue As DebugValue, ByRef AttachedTo As Object, Recursive As Boolean, IsCollectionOf As Boolean, Index As Integer) As DebugWatcher
        If Parent IsNot Nothing AndAlso Not Parent.ChildWatcherValues.Contains(ParentValue.Guid) Then
            Dim w As New DebugWatcher(Parent, ParentValue.Guid, AttachedTo, Recursive, Index, IsCollectionOf, True)
            Return w
        End If
        Return Nothing
    End Function

    Public Shared Function CreateChild(Parent As DebugWatcher, ParentValue As DebugValue, ByRef AttachedTo As Object, Recursive As Boolean) As DebugWatcher
        Return CreateChild(Parent, ParentValue, AttachedTo, Recursive, False, -1)
    End Function

    Private Sub DisposeWorkers()
        If Workers.Count > 0 Then
            For i As Integer = Workers.Count - 1 To 0 Step -1
                Workers(i).Dispose()
                Workers.RemoveAt(i)
            Next
        End If
    End Sub

    Private Sub CreateWorkers()
        DisposeWorkers()
        If IsCollectionOf Then
            Dim timer As New Timer(New TimerCallback(AddressOf Calculate_CollectionWorker), AttachedObject, 0, Timeout.Infinite)
            Workers.Add(timer)
        Else
            For i As Integer = 0 To Fields.Length - 1
                Dim index As Integer = i
                Dim f As FieldReference = Fields(index)
                If Not IgnoreTypes.Contains(f.Info.FieldType) Then
                    Dim timer As New Timer(New TimerCallback(AddressOf Calculate_FieldWorker), f, 0, Timeout.Infinite)
                    Workers.Add(timer)
                End If
            Next

            For i As Integer = 0 To Properties.Count - 1
                Dim index As Integer = i
                Dim bw As New BackgroundWorker With {.WorkerSupportsCancellation = True}
                Dim p As PropertyReference = Properties(index)
                If Not IgnoreTypes.Contains(p.Info.PropertyType) Then
                    Dim timer As New Timer(New TimerCallback(AddressOf Calculate_PropertyWorker), p, 0, Timeout.Infinite)
                    Workers.Add(timer)
                End If
            Next
        End If
    End Sub

#End Region

#Region "Background Workers"

    Private Workers As New List(Of Timer)

    Private Sub Calculate_CollectionWorker(ArrayOrList As Object)
        If ArrayOrList IsNot Nothing Then
            Dim Out As New List(Of DebugValue)
            For i As Integer = 0 To ArrayOrList.Length - 1
                Out.Add(DebugValue.NewArrayOrListValue(Me, i, CollectionType, ArrayOrList, IsRecursive))
            Next
            RaiseEvent CollectionCalculated(Me, Out, ParentValueGuid)
        End If
    End Sub

    Private Sub Calculate_FieldWorker(state As Object)
        Do While isEnabled
            Dim StartTime As DateTime = DateTime.UtcNow
            Dim Reference As FieldReference = DirectCast(state, FieldReference)
            Dim v As DebugValue = CurrentFieldValue(Reference)
            Dim Endtime As DateTime
            Dim ResultInterval As TimeSpan
            Dim ms As Double = 0

            If NotNothing(v) Then
                If v.Guid IsNot Nothing Then
                    If Not ValueTimeline.Timelines.ContainsKey(v.Guid) Then
                        SyncLock (ValueTimeline.Timelines)
                            Dim value As DebugValue = v.Clone()
                            ValueTimeline.Timelines.Add(value.Guid, New ValueTimeline(value.Guid, value))
                            Endtime = DateTime.UtcNow
                            ResultInterval = TimeSpan.FromTicks(Endtime.Ticks - StartTime.Ticks)
                            ms = ResultInterval.TotalMilliseconds
                            value.CalculationTime = ResultInterval
                            OnValueCalculated(Me, value)
                            If value.ValueChanged Then OnChangedValue(Me, value)
                        End SyncLock
                    Else
                        Dim value As DebugValue = v.Clone()
                        ValueTimeline.Timelines(value.Guid).AddValue(value)
                        Endtime = DateTime.UtcNow
                        ResultInterval = TimeSpan.FromTicks(Endtime.Ticks - StartTime.Ticks)
                        ms = ResultInterval.TotalMilliseconds
                        value.CalculationTime = ResultInterval
                        OnValueCalculated(Me, value)
                        If value.ValueChanged Then OnChangedValue(Me, value)
                    End If
                End If
            End If
            If ms < IntervalMilliseconds Then
                Dim diff As Double = Math.Min(Math.Max(0, Interval.TotalMilliseconds - ResultInterval.TotalMilliseconds), Interval.TotalMilliseconds)
                Dim ActualWaitTime As TimeSpan = TimeSpan.FromMilliseconds(diff)
                If diff > 0 Then Thread.Sleep(ActualWaitTime)
            Else
                Thread.Sleep(MinimumInterval)
            End If
        Loop
    End Sub

    Private Sub Calculate_PropertyWorker(state As Object)
        Do While isEnabled
            Dim StartTime As DateTime = DateTime.UtcNow
            Dim Reference As PropertyReference = DirectCast(state, PropertyReference)
            Dim v As DebugValue = CurrentPropertyValue(Reference)
            Dim Endtime As DateTime
            Dim ResultInterval As TimeSpan
            Dim ms As Double = 0

            If NotNothing(v) Then
                If v.Guid IsNot Nothing Then
                    If Not ValueTimeline.Timelines.ContainsKey(v.Guid) Then
                        SyncLock (ValueTimeline.Timelines)
                            Dim value As DebugValue = v.Clone()
                            ValueTimeline.Timelines.Add(value.Guid, New ValueTimeline(value.Guid, value))
                            Endtime = DateTime.UtcNow
                            ResultInterval = TimeSpan.FromTicks(Endtime.Ticks - StartTime.Ticks)
                            ms = ResultInterval.TotalMilliseconds
                            value.CalculationTime = ResultInterval
                            OnValueCalculated(Me, value)
                            If value.ValueChanged Then OnChangedValue(Me, value)
                        End SyncLock
                    Else
                        Dim value As DebugValue = v.Clone()
                        ValueTimeline.Timelines(value.Guid).AddValue(value)
                        Endtime = DateTime.UtcNow
                        ResultInterval = TimeSpan.FromTicks(Endtime.Ticks - StartTime.Ticks)
                        ms = ResultInterval.TotalMilliseconds
                        value.CalculationTime = ResultInterval
                        OnValueCalculated(Me, value)
                        If value.ValueChanged Then OnChangedValue(Me, value)
                    End If
                End If
            End If
            If ms < IntervalMilliseconds Then
                Dim diff As Double = Math.Min(Math.Max(0, Interval.TotalMilliseconds - ResultInterval.TotalMilliseconds), Interval.TotalMilliseconds)
                Dim ActualWaitTime As TimeSpan = TimeSpan.FromMilliseconds(diff)
                If diff > 0 Then Thread.Sleep(ActualWaitTime)
            Else
                Thread.Sleep(MinimumInterval)
            End If
        Loop
    End Sub

#End Region

#Region "General Logic"

    Public Sub StartSession()
        If Not isEnabled Then isEnabled = True
    End Sub

    Public Sub EndSession()
        If isEnabled Then isEnabled = False
    End Sub

#End Region

    Public Function CurrentFieldValue(f As FieldReference) As DebugValue
        SyncLock (ValueTimeline.Timelines)
            If FieldValues.ContainsKey(f.Index) Then
                Dim StateObject As New ValueWorkerState(ReferenceType.Field) With {.Watcher = Me, .Reference = f, .Instance = AttachedObject}
                Return FieldValues(f.Index).UpdateValue(StateObject)
            Else
                Dim newValue As DebugValue = DebugValue.NewFieldValue(Me, f, AttachedObject, IsRecursive).SetValueChanged(True)
                FieldValues.Add(f.Index, newValue)
                ValueTimeline.Timelines.Add(newValue.Guid, New ValueTimeline(newValue.Guid, newValue))
                Return newValue
            End If
        End SyncLock
    End Function

    Public Function CurrentPropertyValue(p As PropertyReference) As DebugValue
        SyncLock (ValueTimeline.Timelines)
            If PropertyValues.ContainsKey(p.Index) Then
                Dim StateObject As New ValueWorkerState(ReferenceType.Property) With {.Watcher = Me, .Reference = p, .Instance = AttachedObject}
                Return PropertyValues(p.Index).UpdateValue(StateObject)
            Else
                Dim newValue As DebugValue = DebugValue.NewPropertyValue(Me, p, AttachedObject, IsRecursive).SetValueChanged(True)
                PropertyValues.Add(p.Index, newValue)
                ValueTimeline.Timelines.Add(newValue.Guid, New ValueTimeline(newValue.Guid))
                Return newValue
            End If
        End SyncLock
    End Function

End Class
