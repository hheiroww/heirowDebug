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
Imports JackDebug.WPF.Async
Imports JackDebug.WPF.DebugWatcher

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

    Private FieldKeys As New Dictionary(Of FieldReference, DebugValue)

    Private FieldValues As New Dictionary(Of FieldReference, DebugValue)
    Private PropertyValues As New Dictionary(Of PropertyReference, DebugValue)

    Public ReadOnly Property Timelines As New Dictionary(Of String, ValueTimeline)

#End Region

#Region "Shared"

    Public Shared Property EnableDispatcherProperties As Boolean = False
    Public Shared Property BlacklistedTypes As New List(Of Type)
    Public Shared Property Watchers As New List(Of DebugWatcher)
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

    Public Event ValueCalculated(Watcher As DebugWatcher, Value As DebugValue)

    Public Event ValueChanged(Watcher As DebugWatcher, ChangedValue As DebugValue, ArrayIndexies As List(Of Integer))

    Public Sub OnChangedValue(Watcher As DebugWatcher, ChangedValue As DebugValue)
        RaiseEvent ValueChanged(Watcher, ChangedValue, ChangedValue.ChangedIndexies)
    End Sub

    Public Sub OnValueCalculated(Watcher As DebugWatcher, ChangedValue As DebugValue)
        RaiseEvent ValueCalculated(Watcher, ChangedValue)
    End Sub

#End Region

#Region "Properties"

    Public ReadOnly Property GUID As String = System.Guid.NewGuid().ToString()
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

    Public Property isEnabled As Boolean
        Get
            Return _isEnabled
        End Get
        Set(value As Boolean)
            _isEnabled = value
            If isEnabled Then CreateWorkers()
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

    Public Property FramesPerSecond
        Get
            Return _FramesPerSecond
        End Get
        Set(value)
            _FramesPerSecond = value
            _IntervalMilliseconds = 1000 / FramesPerSecond
            _Interval = TimeSpan.FromMilliseconds(IntervalMilliseconds)
        End Set
    End Property
    Private _FramesPerSecond As Integer = 30
    Public ReadOnly Property IntervalMilliseconds As Double
        Get
            Return _IntervalMilliseconds
        End Get
    End Property
    Private _IntervalMilliseconds As Double = 1000 / FramesPerSecond

    Public ReadOnly Property Interval As TimeSpan
        Get
            Return _Interval
        End Get
    End Property
    Private _Interval As TimeSpan = TimeSpan.FromMilliseconds(IntervalMilliseconds)

    Public Shared ReadOnly Property MinimumInterval As TimeSpan = TimeSpan.FromTicks(2500)

#End Region

#Region "Initializers"

    Public Sub New(AttachedTo As Object)
        Dim CoreCount As Integer = Environment.ProcessorCount
        ThreadPool.SetMaxThreads(CoreCount, CoreCount)
        Initialize(AttachedTo)
    End Sub

    Public Sub New(AttachedTo As Object, Recursive As Boolean)
        _Recursive = Recursive
        Initialize(AttachedTo)
    End Sub

    Private Sub Initialize(ByRef AttachedTo As Object, Optional Name As String = "")
        _Name = If(Name = "", AttachedTo.GetType().ToString(), Name)
        _AttachedObject = AttachedTo
        DeserializationWrapper.ReflectionFlags = BindingFlags.Instance Or BindingFlags.[Public] Or BindingFlags.NonPublic
        _Fields = DeserializationWrapper.GetFieldReferences(AttachedObject.GetType()).ToArray()
        _Properties = DeserializationWrapper.GetPropertyReferences(AttachedObject.GetType()).ToArray()

        ValueCount = If(Fields IsNot Nothing, _Fields.Count, 0) + If(Properties IsNot Nothing, _Properties.Count, 0)
        DebugWatcher.Watchers.Add(Me)
        If isEnabled Then CreateWorkers()
    End Sub

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
                Dim FieldState As New FieldState(index, f)
                Dim timer As New Timer(New TimerCallback(AddressOf FieldWorker_Calculate), FieldState, 0, Timeout.Infinite)
                Workers.Add(timer)
            End If

        Next

        For i As Integer = 0 To Properties.Count - 1
            Dim index As Integer = i
            Dim bw As New BackgroundWorker With {.WorkerSupportsCancellation = True}
            Dim p As PropertyReference = Properties(index)
            If Not DebugValue.IgnoreTypes.Contains(p.Info.PropertyType) Then
                Dim PropertyState As New PropertyState(index, p)
                Dim timer As New Timer(New TimerCallback(AddressOf PropertyWorker_Calculate), PropertyState, 0, Timeout.Infinite)
                Workers.Add(timer)
            End If

        Next
    End Sub

#End Region

#Region "Background Workers"

    Private Workers As New List(Of Timer)

    Private Sub FieldWorker_Calculate(state As Object)
        Do While isEnabled
            Dim StartTime As DateTime = DateTime.Now
            Dim FieldState As FieldState = DirectCast(state, FieldState)
            Dim v As DebugValue = CurrentFieldValue(FieldState.f)

            If NotNothing(v) AndAlso v.Value IsNot Nothing Then
                If v.GUID IsNot Nothing Then
                    If Not Timelines.ContainsKey(v.GUID) Then
                        SyncLock (Timelines)
                            Dim value As DebugValue = v.Clone()
                            Timelines.Add(value.GUID, New ValueTimeline(value.GUID, value))
                            OnValueCalculated(Me, value)
                            If value.ValueChanged Then OnChangedValue(Me, value)
                        End SyncLock
                    Else
                        Dim value As DebugValue = v.Clone()
                        Timelines(value.GUID).AddValue(value)
                        OnValueCalculated(Me, value)
                        If value.ValueChanged Then OnChangedValue(Me, value)
                    End If

                End If
            End If
            Dim Endtime As DateTime = DateTime.Now
            Dim ResultInterval As TimeSpan = TimeSpan.FromTicks(Endtime.Ticks - StartTime.Ticks)
            Dim ms As Double = ResultInterval.TotalMilliseconds
            'ForceUI()
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
            Dim StartTime As DateTime = DateTime.Now
            Dim PropertyState As PropertyState = DirectCast(state, PropertyState)
            Dim v As DebugValue = CurrentPropertyValue(PropertyState.p)

            If NotNothing(v) AndAlso v.Value IsNot Nothing Then

                If v.GUID IsNot Nothing AndAlso v.ValueChanged Then
                    If Not Timelines.ContainsKey(v.GUID) Then
                        SyncLock (Timelines)
                            Dim value As DebugValue = v.Clone()
                            Timelines.Add(value.GUID, New ValueTimeline(value.GUID, value))
                            OnValueCalculated(Me, value)
                        End SyncLock
                    Else
                        Dim value As DebugValue = v.Clone()
                        Timelines(value.GUID).AddValue(value)
                        OnValueCalculated(Me, value)
                    End If
                End If
            End If
            Dim Endtime As DateTime = DateTime.Now
            Dim ResultInterval As TimeSpan = TimeSpan.FromMilliseconds(Endtime.Ticks - StartTime.Ticks)
            Dim ms As Double = ResultInterval.TotalMilliseconds
            If ms < IntervalMilliseconds Then
                Dim diff As Double = Math.Min(Math.Max(0, ResultInterval.TotalMilliseconds - IntervalMilliseconds), IntervalMilliseconds)
                Dim ActualWaitTime As TimeSpan = TimeSpan.FromMilliseconds(diff)
                Thread.Sleep(ActualWaitTime)
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
        SyncLock (FieldValues)
            SyncLock (Timelines)
                If FieldValues.ContainsKey(f) Then
                    Dim StateObject As New ChildValueWorkerState With {.Reference = f, .Parents = {AttachedObject}, .Instance = AttachedObject, .Type = ReferenceType.Field}
                    Return FieldValues(f).UpdateValue(StateObject)
                Else
                    Dim newValue As DebugValue = DebugValue.NewFieldValue(f, AttachedObject, IsRecursive).SetValueChanged(True)
                    FieldValues.Add(f, newValue)
                    Timelines.Add(newValue.GUID, New ValueTimeline(newValue.GUID, newValue))
                    Return newValue
                End If
            End SyncLock
        End SyncLock
    End Function

    Public Function CurrentPropertyValue(p As PropertyReference) As DebugValue
        SyncLock (PropertyValues)
            SyncLock (Timelines)
                If PropertyValues.ContainsKey(p) Then
                    Dim StateObject As New ChildValueWorkerState With {.Reference = p, .Instance = AttachedObject, .Parents = {AttachedObject}, .Type = ReferenceType.Property}
                    Return PropertyValues(p).UpdateValue(StateObject)
                Else
                    Dim newValue As DebugValue = DebugValue.NewPropertyValue(p, AttachedObject, IsRecursive).SetValueChanged(True)
                    PropertyValues.Add(p, newValue)
                    Timelines.Add(newValue.GUID, New ValueTimeline(newValue.GUID))
                    Return newValue
                End If
            End SyncLock
        End SyncLock
    End Function

End Class
