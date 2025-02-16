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

    Private FieldValues As New SortedDictionary(Of Integer, DebugValue)
    Private PropertyValues As New SortedDictionary(Of Integer, DebugValue)

    Public ChildWatcherValues As New List(Of String)
    Public ChildWatcherGuids As New List(Of String)

#End Region

#Region "Shared"

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
    ''' Fires when the watcher has a new value.
    ''' </summary>
    ''' <param name="Watcher">The associated Watcher</param>
    ''' <param name="Value">The Value that was calculated/processed</param>
    Public Event ValueCalculated(Watcher As DebugWatcher, Value As DebugValue)

    ''' <summary>
    ''' Detects main & child Item Value Changes
    ''' </summary>
    ''' <param name="Watcher">The associated Watcher</param>
    ''' <param name="Value">The Value that changed</param>
    ''' <param name="ArrayIndexies">The Index(s) that have changed</param>
    Public Event ValueChanged(Watcher As DebugWatcher, Value As DebugValue, ArrayIndexies As List(Of Integer))

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

    Public ReadOnly Property isChild As Boolean
        Get
            Return _isChild
        End Get
    End Property
    Private _isChild As Boolean = False

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
    Public Sub New(Parent As DebugWatcher, ParentValueGuid As String, AttachedTo As Object, Recursive As Boolean)
        _Recursive = Recursive
        _Parent = Parent
        _isChild = True
        _ParentValueGuid = ParentValueGuid
        Parent.ChildWatcherValues.Add(ParentValueGuid)
        Parent.ChildWatcherGuids.Add(Guid)
        Initialize(AttachedTo)
        isEnabled = Parent.isEnabled
    End Sub

    Private Sub Initialize(ByRef AttachedTo As Object, Optional Name As String = "")
        _Name = If(Name = "", AttachedTo.GetType().ToString(), Name)
        _AttachedObject = AttachedTo
        DeserializationWrapper.ReflectionFlags = BindingFlags.Instance Or BindingFlags.[Public] Or BindingFlags.NonPublic

        _Fields = DeserializationWrapper.GetFieldReferences(AttachedObject.GetType()).ToArray()
        _Properties = DeserializationWrapper.GetPropertyReferences(AttachedObject.GetType()).ToArray()

        ValueCount = If(Fields IsNot Nothing, _Fields.Count, 0) + If(Properties IsNot Nothing, _Properties.Count, 0)
        If NotNothing(Parent) Then
            isEnabled = Parent.isEnabled
        End If
        DebugWatcher.Watchers.Add(Guid, Me)

    End Sub

    Public Shared Function CreateChild(Parent As DebugWatcher, ParentValueGuid As String, ByRef AttachedTo As Object, Recursive As Boolean) As DebugWatcher
        If Parent IsNot Nothing AndAlso Not Parent.ChildWatcherValues.Contains(ParentValueGuid) Then
            Dim w As New DebugWatcher(Parent, ParentValueGuid, AttachedTo, Recursive)
            Return w
        End If
        Return Nothing
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
        For i As Integer = 0 To Fields.Length - 1
            Dim index As Integer = i

            Dim f As FieldReference = Fields(index)
            If Not DebugValue.IgnoreTypes.Contains(f.Info.FieldType) Then
                Dim timer As New Timer(New TimerCallback(AddressOf FieldWorker_Calculate), f, 0, Timeout.Infinite)
                Workers.Add(timer)
            End If
        Next

        For i As Integer = 0 To Properties.Count - 1
            Dim index As Integer = i
            Dim bw As New BackgroundWorker With {.WorkerSupportsCancellation = True}
            Dim p As PropertyReference = Properties(index)
            If Not DebugValue.IgnoreTypes.Contains(p.Info.PropertyType) Then
                Dim timer As New Timer(New TimerCallback(AddressOf PropertyWorker_Calculate), p, 0, Timeout.Infinite)
                Workers.Add(timer)
            End If
        Next
    End Sub

#End Region

#Region "Background Workers"

    Private Workers As New List(Of Timer)

    Private Sub FieldWorker_Calculate(state As Object)
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

    Private Sub PropertyWorker_Calculate(state As Object)
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
                Dim StateObject As New ValueWorkerState(ReferenceType.Field) With {.Reference = f, .Instance = AttachedObject}
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
                Dim StateObject As New ValueWorkerState(ReferenceType.Property) With {.Reference = p, .Instance = AttachedObject}
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
