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

Public Class DebugWatcher

#Region "Caches"

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

    Public Event OnValueCalculated(Watcher As DebugWatcher, Value As DebugValue)

#End Region

#Region "Properties"

    Public ReadOnly Property GUID As String = System.Guid.NewGuid().ToString()
    Public Property ValueCount As Integer = 0

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

    Public Property Recursive As Boolean
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
        _Fields = DeserializationWrapper.GetFieldReferences(AttachedObject).ToArray()
        _Properties = DeserializationWrapper.GetPropertyReferences(AttachedObject).ToArray()

        ValueCount = If(Fields IsNot Nothing, _Fields.Count, 0) + If(Properties IsNot Nothing, _Properties.Count, 0)
        DebugWatcher.Watchers.Add(Me)
        If isEnabled Then CreateWorkers()
    End Sub

    Private Sub CreateWorkers()
        If Workers.Count > 0 Then
            For i As Integer = Workers.Count - 1 To 0 Step -1
                Workers(i).Dispose()
                Workers.RemoveAt(i)
            Next
        End If

        For i As Integer = 0 To Fields.Length - 1
            Dim index As Integer = i
            Dim bw As New BackgroundWorker With {.WorkerSupportsCancellation = True}
            AddHandler bw.DoWork, Sub() FieldWorker_Calculate(index)
            bw.RunWorkerAsync()
        Next

        For i As Integer = 0 To Properties.Count - 1
            Dim index As Integer = i
            Dim bw As New BackgroundWorker With {.WorkerSupportsCancellation = True}
            AddHandler bw.DoWork, Sub() PropertyWorker_Calculate(index)
            bw.RunWorkerAsync()
        Next
    End Sub

#End Region

#Region "Background Workers"

    Private Workers As New List(Of BackgroundWorker)

    Private Sub FieldWorker_Calculate(index As Integer)
        Do While isEnabled
            Dim StartTime As DateTime = DateTime.Now
            Dim f As FieldReference = Fields(index)
            Dim v As DebugValue = CurrentFieldValue(f).Clone()
            If NotNothing(v) Then
                If v.GUID IsNot Nothing AndAlso v.ValueChanged Then
                    SyncLock (Timelines)
                        Timelines(v.GUID).AddValue(v)
                    End SyncLock
                    RaiseEvent OnValueCalculated(Me, v)
                End If
            End If
            Dim Endtime As DateTime = DateTime.Now
            Dim ResultInterval As TimeSpan = TimeSpan.FromTicks(Endtime.Ticks - StartTime.Ticks)
            Dim ms As Double = ResultInterval.TotalMilliseconds
            If ms < IntervalMilliseconds Then
                Dim diff As Double = Math.Min(Math.Max(0, Interval.TotalMilliseconds - ResultInterval.TotalMilliseconds), Interval.TotalMilliseconds)
                Dim ActualWaitTime As TimeSpan = TimeSpan.FromMilliseconds(diff)
                If diff > 0 Then Thread.Sleep(ActualWaitTime)
            Else
                Thread.Sleep(MinimumInterval)
            End If
        Loop
    End Sub

    Private Sub PropertyWorker_Calculate(index As Integer)
        Do While isEnabled
            Dim StartTime As DateTime = DateTime.Now
            Dim p As PropertyReference = Properties(index)
            Dim v As DebugValue = CurrentPropertyValue(p).Clone()

            If NotNothing(v) Then
                If v.GUID IsNot Nothing AndAlso v.ValueChanged Then
                    SyncLock (Timelines)
                        Timelines(v.GUID).AddValue(v)
                    End SyncLock
                    RaiseEvent OnValueCalculated(Me, v)
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
            If FieldValues.ContainsKey(f) Then
                FieldValues(f).UpdateValue(AttachedObject, f)
            Else
                Dim newValue As DebugValue = DebugValue.NewFieldValue(f, AttachedObject)
                FieldValues.Add(f, newValue)
                SyncLock (Timelines)
                    Timelines.Add(newValue.GUID, New ValueTimeline(newValue.GUID))
                End SyncLock
            End If
            Return FieldValues(f)
        End SyncLock
    End Function

    Public Function CurrentPropertyValue(p As PropertyReference) As DebugValue
        SyncLock (PropertyValues)
            If PropertyValues.ContainsKey(p) Then
                PropertyValues(p).UpdateValue(AttachedObject, p)
            Else
                Dim newValue As DebugValue = DebugValue.NewPropertyValue(p, AttachedObject)
                PropertyValues.Add(p, newValue)
                SyncLock (Timelines)
                    Timelines.Add(newValue.GUID, New ValueTimeline(newValue.GUID))
                End SyncLock
            End If
            Return PropertyValues(p)
        End SyncLock
    End Function

End Class
